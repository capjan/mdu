# Contributing to MDU

Thanks for your interest in MDU! Bug reports, ideas, documentation fixes, and code are all welcome.

## Ways to help

- **Report a bug or ask a question** in the [issue tracker](https://github.com/capjan/mdu/issues). Include the `mdu --version` output, the command you ran, and a small Markdown example if possible.
- **Pick an issue** labeled [`good first issue`](https://github.com/capjan/mdu/labels/good%20first%20issue) or [`help wanted`](https://github.com/capjan/mdu/labels/help%20wanted). Comment on it so others know you are working on it.
- **Improve the documentation** in the [`Docs`](Docs) folder. It is plain Markdown and rendered by MDU itself.
- **Tell others** about MDU, or add your project to a discussion if it uses MDU.

## Development setup

The [.NET 10 SDK](https://dotnet.microsoft.com/download) is all you need to build, test, and run MDU.

```bash
dotnet restore Mdu.sln
dotnet build Mdu.sln
dotnet test Mdu.sln
dotnet doctor fix            # installs the local Git hooks
```

Preview the documentation with your local build:

```bash
dotnet run --project Source/Mdu -- serve Docs
```

Open the preview at `http://127.0.0.1:<port>/`.

[`AGENTS.md`](AGENTS.md) describes the architecture and the repository conventions in more detail.

## Pull requests

- Keep a pull request focused on one change and add tests for new behavior.
- Commit messages and pull request titles follow [Conventional Commits](https://www.conventionalcommits.org/), for example `feat(renderer): add …` or `fix(lint): …`. The changelog and the version are generated from them.
- Keep existing CLI commands, aliases, and arguments working.
- Build tools, generators, and tests use .NET only.

By contributing, you agree that your contribution is released under the [MIT License](LICENSE).
