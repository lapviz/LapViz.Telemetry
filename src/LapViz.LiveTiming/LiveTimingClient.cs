using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LapViz.LiveTiming.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace LapViz.LiveTiming;

public class LiveTimingClient : IDisposable, INotifyPropertyChanged
{
    private HubConnection _connection;
    private string _wsUri;
    private readonly ILogger<LiveTimingClient> _logger;
    private readonly ConcurrentQueue<SessionDataDeviceDto> _timingDataQueue;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _backgroundWorker;

    public LiveTimingClient(ILogger<LiveTimingClient> logger)
    {
        _logger = logger;
        _timingDataQueue = new ConcurrentQueue<SessionDataDeviceDto>();
        _cancellationTokenSource = new CancellationTokenSource();
        _backgroundWorker = Task.Run(ProcessQueueAsync);
    }

    private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);

    public async Task<bool> ConnectAsync(string wsUri, bool ignoreCertificateErrors = false, TimeSpan? initialConnectTimeout = null)
    {
        _wsUri = wsUri;

        await _connectionLock.WaitAsync();
        try
        {
            _logger.LogInformation("LiveTimingClient Initial Connection Starting");

            if (_connection != null)
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
            }

            _connection = new HubConnectionBuilder()
                .WithUrl(_wsUri, opts =>
                {
                    if (ignoreCertificateErrors)
                    {
                        opts.HttpMessageHandlerFactory = (message) =>
                        {
                            if (message is HttpClientHandler clientHandler)
                            {
                                clientHandler.ServerCertificateCustomValidationCallback +=
                                    (sender, certificate, chain, sslPolicyErrors) => true;
                            }
                            return message;
                        };
                    }
                })
                .WithAutomaticReconnect()
                    .ConfigureLogging(b =>
                    {
                        //b.AddDebug(); Microsoft.Extensions.Logging.Debug to be added later
                        b.SetMinimumLevel(LogLevel.Debug);
                    })
                .Build();

            RegisterConnectionHandlers();

            var startTimeout = initialConnectTimeout ?? TimeSpan.FromSeconds(30);
            var startTime = DateTime.UtcNow;
            int attempt = 0;

            while (_connection.State != HubConnectionState.Connected &&
                   DateTime.UtcNow - startTime < startTimeout)
            {
                try
                {
                    attempt++;
                    _logger.LogInformation("LiveTimingClient - Attempt {Attempt} to start connection...", attempt);
                    if (_connection.State == HubConnectionState.Disconnected)
                    {
                        await _connection.StartAsync();
                    }

                    if (_connection.State == HubConnectionState.Connected)
                    {
                        _logger.LogInformation("LiveTimingClient Connected Successfully");
                        UpdateState();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    int delayMs = Math.Min(10000, 1000 * attempt);
                    _logger.LogWarning(ex, "Initial connection attempt {attempt} failed. Retrying in {delay}ms...", attempt, delayMs);
                    OnError("InitialConnectFailed", $"Attempt {attempt} failed: {ex.Message}");
                    UpdateState();
                    await Task.Delay(delayMs);
                }
            }

            if (_connection.State != HubConnectionState.Connected)
            {
                _logger.LogError("LiveTimingClient failed to connect after timeout.");
                UpdateState();
                return false;
            }

            return true;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void RegisterConnectionHandlers()
    {
        _connection.On<SessionDataDeviceDto>("sessionEvent", sessionDeviceDataEvent =>
        {
            _logger.LogInformation("Event received {sessionDeviceDataEvent}", sessionDeviceDataEvent);
            OnSessionDeviceDataReceived(sessionDeviceDataEvent);
            return Task.CompletedTask;
        });

        _connection.On<DeviceInfoDto>("deviceInfoUpdated", deviceInfo =>
        {
            _logger.LogInformation("Device info update received {deviceInfo}", deviceInfo);
            OnDeviceInfoUpdated(deviceInfo);
            return Task.CompletedTask;
        });

        _connection.On<SessionDataDto>("board", sessionBoard =>
        {
            _logger.LogInformation("Board update received");
            OnBoardUpdated(sessionBoard);
            return Task.CompletedTask;
        });

        _connection.On<string, string>("UserJoined", (connectionId, joinedSessionId) =>
        {
            _logger.LogInformation("{connectionId} joined session {joinedSessionId}", connectionId, joinedSessionId);
            UserJoined?.Invoke(this, connectionId);
        });

        _connection.On<string, string>("UserLeft", (connectionId, leftSessionId) =>
        {
            _logger.LogInformation("{connectionId} left session {leftSessionId}", connectionId, leftSessionId);
            UserLeft?.Invoke(this, connectionId);
        });

        _connection.Reconnected += _connection_Reconnected;
        _connection.Reconnecting += _connection_Reconnecting;
        _connection.Closed += async (error) =>
        {
            UpdateState();
            await Task.Delay(new Random().Next(0, 5) * 1000);
            await TryReconnectAndJoinSessions();
        };
    }

    private void UpdateState()
    {
        ConnectionStateChanged?.Invoke(this, State);
        _logger.LogInformation("Connection state changed: {State}", State);
    }

    private Task _connection_Reconnecting(Exception arg)
    {
        _logger.LogDebug("Reconnecting");
        UpdateState();

        return Task.CompletedTask;
    }

    private async Task _connection_Reconnected(string arg)
    {
        _logger.LogDebug("Reconnected");
        UpdateState();

        foreach (var joinedSession in JoinedSessions)
        {
            await JoinSession(joinedSession.Key, joinedSession.Value);
        }
    }

    private readonly SemaphoreSlim _disconnectionLock = new SemaphoreSlim(1, 1);
    public async Task<bool> DisconnectAsync()
    {
        await _disconnectionLock.WaitAsync();

        try
        {
            if (_connection == null)
                return false;

            if (_connection.State == HubConnectionState.Disconnected)
                return false;

            foreach (var joinedSession in JoinedSessions)
            {
                await LeaveSession(joinedSession.Key);
            }

            await _connection.StopAsync();

            JoinedSessions.Clear();
            UpdateState();

            return true;
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to disconnect: " + ex.Message);
        }
        finally
        {
            _disconnectionLock.Release();
        }
    }

    public virtual async Task<string> CreateSession(SessionCreateRequestDto createRequestDto)
    {
        try
        {
            _logger.LogDebug("Creating session");

            if (_connection == null || _connection.State != HubConnectionState.Connected)
                throw new Exception("Can't create session: not connected");

            var sessionId = await _connection.InvokeAsync<string>("CreateSession", createRequestDto);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                OnError("CreateSessionFailed", "Server returned an error");
                throw new Exception("Failed to join session. Server returned an error");
            }

            _logger.LogInformation("Session {sessionId} created", sessionId);

            await JoinSession(sessionId, createRequestDto.Password);
            return sessionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session creation failed: {message}", ex.Message);
            OnError("CreateSessionFailed", ex.Message);
            throw;
        }
    }

    public virtual async Task<string> GetCircuitPublicSession(string circuitCode)
    {
        try
        {
            _logger.LogDebug("Getting circuit session code");

            if (_connection == null || _connection.State != HubConnectionState.Connected)
                throw new Exception("Can't get circuit public session: not connected");

            var sessionId = await _connection.InvokeAsync<string>("GetCircuitPublicSession", circuitCode);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                OnError("GetCircuitPublicSessionFailed", "Server returned an error");
                throw new Exception("Failed get circuit public session. Server returned an error");
            }

            _logger.LogInformation("Session {sessionId} retrieved", sessionId);

            await JoinSession(sessionId, null);
            return sessionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed get circuit public session: {message}", ex.Message);
            OnError("GetCircuitPublicSessionFailed", ex.Message);
            throw;
        }
    }

    public virtual async Task JoinSession(string sessionId, string password)
    {
        try
        {
            _logger.LogDebug("Joining session {session}", sessionId);

            if (_connection != null && _connection.State == HubConnectionState.Connected)
            {
                var result = await _connection.InvokeAsync<bool>("JoinSession", sessionId, password);
                if (!result)
                    throw new Exception("Failed to join session. Server returned an error");

                if (!JoinedSessions.ContainsKey(sessionId))
                    JoinedSessions.Add(sessionId, password);
            }
            else
            {
                _logger.LogDebug("Can't join session {session} not connected", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Join session failed: {message}", ex.Message);
            OnError("JoinSessionFailed", ex.Message);
        }
    }

    public virtual async Task LeaveSession(string sessionId)
    {
        try
        {
            _logger.LogDebug("Parting session {session}", sessionId);

            if (_connection != null && _connection.State == HubConnectionState.Connected)
            {
                var result = await _connection.InvokeAsync<bool>("LeaveSession", sessionId);
                if (!result)
                    throw new Exception("Failed to leave session. Server returned an error");

                if (JoinedSessions.ContainsKey(sessionId))
                    JoinedSessions.Remove(sessionId);
            }
            else
            {
                _logger.LogDebug("Can't leave session {session} not connected", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Leave session failed: {message}", ex.Message);
            OnError("LeaveSessionFailed", ex.Message);
        }
    }

    private async Task TryReconnectAndJoinSessions()
    {
        if (JoinedSessions.Count == 0)
            return;

        const int maxRetries = 5;
        int retryCount = 0;

        while (_connection.State == HubConnectionState.Disconnected && retryCount < maxRetries)
        {
            try
            {
                _logger.LogInformation("Attempt {retryCount} to reconnect...", retryCount + 1);
                await _connection.StartAsync();

                foreach (var joinedSession in JoinedSessions)
                {
                    await JoinSession(joinedSession.Key, joinedSession.Value);
                }
            }
            catch (Exception ex)
            {
                retryCount++;
                int delay = (int)Math.Pow(2, retryCount) * 1000;
                _logger.LogError(ex, "Reconnect attempt {retryCount} failed. Waiting {delay}ms...", retryCount, delay);
                OnError("TryReconnectAndJoinSessionFailed", ex.Message);
                await Task.Delay(delay);
            }
        }

        _logger.LogError("Max reconnect attempts reached. Giving up.");
    }


    public virtual async Task UpdateDeviceInfo(DeviceInfoDto deviceInfo)
    {
        try
        {
            var result = await _connection.InvokeAsync<bool>("UpdateDeviceInfo", deviceInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateDeviceInfo failed: {message}", ex.Message);
            OnError("UpdateDeviceInfoFailed", ex.Message);
        }
    }

    public virtual async Task RequestDeviceInfo(string sessionId, string deviceId)
    {
        try
        {
            await _connection.SendAsync("GetDeviceInfo", sessionId, deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RequestDeviceInfo failed: {message}", ex.Message);
            OnError("RequestDeviceInfoFailed", ex.Message);
        }
    }

    public void AddEventData(SessionDataDeviceDto sessionDeviceDataDto)
    {
        try
        {
            _timingDataQueue.Enqueue(sessionDeviceDataDto);
            QueueSize = _timingDataQueue.Count;
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    private async Task SendEventData(SessionDataDeviceDto data)
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _cancellationTokenSource.Token);

                if (_connection != null && _connection.State == HubConnectionState.Connected)
                {
                    var result = await _connection.InvokeAsync<bool>("addEventData", data, linkedCts.Token);
                    if (!result)
                        throw new Exception("Failed to add events to server. Server returned an error.");
                    MessagesSent++;
                    _logger.LogDebug("Data sent successfully.");
                    return;
                }
                else
                {
                    _logger.LogWarning("Connection not ready. Retrying in 1s...");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Send timed out. Retrying...");
                OnError("SendEventDataTimedOut", "Send timed out. Retrying...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Send failed. Retrying...");
                OnError("SendEventDataFailed", ex.Message);
            }

            await Task.Delay(1000);
        }
    }

    private async Task ProcessQueueAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (_timingDataQueue.TryDequeue(out var timingData))
                {
                    QueueSize = _timingDataQueue.Count;

                    await SendEventData(timingData);
                    _logger.LogDebug("Data sent via SendEventData.");
                }
                else
                {
                    await Task.Delay(100);
                }
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to process queue: {message}", ex.Message);
                //throw;
            }
        }
    }

    ~LiveTimingClient()
    {
        _cancellationTokenSource.Cancel();
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        try
        {
            _backgroundWorker?.Wait();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background worker didn't terminate cleanly.");
        }

        if (_connection != null)
        {
            try
            {
                _connection.StopAsync().Wait();
                _connection.DisposeAsync().AsTask().Wait();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during connection dispose.");
            }
        }
        while (_timingDataQueue.TryDequeue(out _)) { }
    }

    #region Properties

    public IDictionary<string, string> JoinedSessions { get; private set; } = new Dictionary<string, string>();

    private int _queueSize = 0;
    public int QueueSize
    {
        get => _queueSize;
        private set
        {
            if (_queueSize != value)
            {
                _queueSize = value;
                OnPropertyChanged();
            }
        }
    }


    private int _messagesSent;
    public int MessagesSent
    {
        get => _messagesSent;
        private set
        {
            if (_messagesSent != value)
            {
                _messagesSent = value;
                OnPropertyChanged();
            }
        }
    }

    public LiveTimingClientState State
    {
        get
        {
            if (_connection == null || _connection.State == HubConnectionState.Disconnected)
            {
                return LiveTimingClientState.Disconnected;
            }

            if (_connection.State == HubConnectionState.Connected)
            {
                return LiveTimingClientState.Connected;
            }

            return LiveTimingClientState.Connecting;
        }
    }

    #endregion

    #region Events
    /// <summary>
    /// Occurs when [data received].
    /// </summary>
    public event EventHandler<SessionDataDeviceDto> SessionEventReceived;
    /// <summary>Raises the <see cref="E:DataReceived" /> event.</summary>
    /// <param name="e">The <see cref="SessionDataDeviceDto" /> instance containing the event data.</param>
    protected virtual void OnSessionDeviceDataReceived(SessionDataDeviceDto e)
    {
        SessionEventReceived?.Invoke(this, e);
    }

    /// <summary>
    /// Occurs when [data received].
    /// </summary>
    public event EventHandler<DeviceInfoDto> DeviceInfoUpdated;
    /// <summary>Raises the <see cref="E:DataReceived" /> event.</summary>
    /// <param name="e">The <see cref="DeviceInfoDto" /> instance containing the event data.</param>
    protected virtual void OnDeviceInfoUpdated(DeviceInfoDto e)
    {
        DeviceInfoUpdated?.Invoke(this, e);
    }

    /// <summary>
    /// Occurs when [data received].
    /// </summary>
    public event EventHandler<SessionDataDto> BoardUpdated;
    /// <summary>Raises the <see cref="E:DataReceived" /> event.</summary>
    /// <param name="e">The <see cref="SessionDataDto" /> instance containing the event data.</param>
    protected virtual void OnBoardUpdated(SessionDataDto e)
    {
        BoardUpdated?.Invoke(this, e);
    }

    public event EventHandler<LiveTimingClientError> ErrorOccurred;

    protected virtual void OnError(string code, string message)
    {
        var error = new LiveTimingClientError(code, message, State);
        ErrorOccurred?.Invoke(this, error);
    }

    public event EventHandler<LiveTimingClientState> ConnectionStateChanged;
    public event EventHandler<string> UserJoined;
    public event EventHandler<string> UserLeft;

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
