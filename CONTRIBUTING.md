# Contributing to LapViz.Telemetry

Thanks for considering contributing to **LapViz.Telemetry**!  
We welcome bug reports, feature requests, and code contributions.

## Table of Contents
- [Code of Conduct](#code-of-conduct)
- [Prerequisites](#prerequisites)
- [How to contribute](#how-to-contribute)
  - [Reporting bugs](#reporting-bugs)
  - [Requesting features](#requesting-features)
  - [Submitting changes](#submitting-changes)
- [Coding guidelines](#coding-guidelines)
  - [Code style](#code-style)
  - [Dependencies](#dependencies)
  - [Unit tests](#unit-tests)
- [Pull request process](#pull-request-process)
- [Acknowledgement](#acknowledgement)

---

## Code of Conduct
By participating in this project you agree to follow our [Code of Conduct](CODE_OF_CONDUCT.md).

---

## Prerequisites
By contributing, you confirm that:
- The contribution is your own original work.
- You have the right to license the work under the same license as the project (MIT).
- You agree that your contribution will be published under the terms of the project’s [LICENSE](LICENSE.md).

---

## How to contribute

### Reporting bugs
- Check the [issue tracker](https://github.com/lapviz/lapviz.telemetry/issues) to avoid duplicates.
- When reporting, include:
  - Steps to reproduce
  - Expected behavior
  - Actual behavior
  - Environment details (.NET version, OS, etc.)

### Requesting features
- Open an issue with the `enhancement` label.
- Clearly describe the problem the feature solves and potential implementation ideas.

### Submitting changes
1. Fork the repository.
2. Create a branch: `git checkout -b feature/my-feature`.
3. Make your changes.
4. Run all tests locally (`dotnet test`).
5. Commit with a clear message.
6. Push and open a Pull Request (PR) against the `main` branch.

---

## Coding guidelines

### Code style
- Follow [Microsoft .NET coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).
- Use `dotnet format` to clean up whitespace and formatting.
- No unrelated reformatting in PRs.

### Dependencies
- Keep dependencies minimal.  
- LapViz libraries should generally only depend on .NET BCL and explicitly approved packages.

### Unit tests
- All new code must include unit tests where applicable.
- Run `dotnet test` before submitting PRs.
- Do not break existing tests.

---

## Pull request process
- Ensure your branch builds successfully with no warnings or errors.
- Ensure all tests are passing (`dotnet test`).
- Update documentation if needed.
- Reference related issues in the PR description.
- Be responsive to feedback from maintainers.
- PRs may require rebasing if `main` has moved forward.

---

## Acknowledgement
This guide is based on community best practices, GitHub templates, and other open source projects.  
Thank you for contributing to LapViz 🚀
