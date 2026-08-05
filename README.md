# CopilotKit <> Microsoft Agent Framework Starter

This is a starter template for building AI agents using [Microsoft Agent Framework](https://github.com/microsoft/agents) and [CopilotKit](https://copilotkit.ai). It provides a modern Next.js application with an integrated proverbs management agent that demonstrates AG-UI protocol features including shared state, generative UI, and human-in-the-loop workflows.

## Prerequisites

- **OpenAI API Key** (for the agent's chat clients)
  - Retrieve from [OpenAI API Keys](https://platform.openai.com/api-keys).
- **.NET 9.0 SDK**
  - [Download directly](https://dotnet.microsoft.com/download/dotnet/9.0)
  - macOS/Linux
    - [Install via Homebrew](https://formulae.brew.sh/formula/dotnet) (`brew install dotnet@9`) or
    - <details><summary>Install via <code>curl</code> install script</summary><br />

      ```bash
      curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 9.0
      export PATH="$HOME/.dotnet:$PATH"
      ```

      </details>

  - Windows
    - [Install via WinGet](https://winstall.app/apps/Microsoft.DotNet.SDK.9) (`winget install --id=Microsoft.DotNet.SDK.9 -e`)

- **Node.js 20+**
  - [Download directly](https://nodejs.org/en/download)
  - macOS/Linux
    - [Install via Homebrew](https://formulae.brew.sh/formula/node@24) (`brew install node@24`) or
    - <details><summary>Install via <code>curl</code> install script</summary><br />

      ```bash
      # Download and install nvm:
      curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.3/install.sh | bash

      # in lieu of restarting the shell
      \. "$HOME/.nvm/nvm.sh"

      # Download and install Node.js:
      nvm install 24
      ```

      </details>

  - Windows
    - [Install via WinGet](https://winstall.app/apps/OpenJS.NodeJS) (`winget install --id=OpenJS.NodeJS -v "24.11.0" -e`)

- Any of the following package managers:
  - [npm](https://docs.npmjs.com/downloading-and-installing-node-js-and-npm) **(default)** (usually installed with Node.js)
  - [pnpm](https://pnpm.io/installation)
  - [yarn](https://classic.yarnpkg.com/lang/en/docs/install/)
  - [bun](https://bun.sh/)

## Getting Started

1. Install dependencies using your preferred package manager:

   ```bash
   # Using npm (default)
   npm install

   # Using pnpm
   pnpm install

   # Using yarn
   yarn install

   # Using bun
   bun install
   ```

   > **Note:** This will automatically setup the C# agent as well (restore NuGet packages).
   >
   > If you have manual issues, you can run:
   >
   > ```sh
   > npm run install:agent
   > ```

2. Set up your OpenAI API key for the agent:

   Navigate to the agent directory and set it as a user secret:

   ```bash
   cd agent
   dotnet user-secrets set "OpenAI:ApiKey" "<your-openai-api-key>"
   cd ..
   ```

   Or export it in your shell (overrides user secrets):

   ```bash
   export OPENAI_API_KEY="<your-openai-api-key>"
   ```

   To use a custom OpenAI-compatible endpoint, set `OPENAI_BASE_URL`
   (defaults to `https://api.openai.com/v1`).

   The agent registers a single shared `IChatClient` as a singleton and both
   factories (proverbs and formFill) inject the same instance. Two implementations
   are available: `OllamaChatClientImpl` (default) and `OpenAIChatClientImpl`.
   The provider is selected via `CHAT__PROVIDER` (`ollama` or `openai`).
   The model is configured via `CHAT__MODEL` (default: `gpt-4o-mini`).

3. Start the development server:

   ```bash
   # Using npm (default)
   npm run dev

   # Using pnpm
   pnpm dev

   # Using yarn
   yarn dev

   # Using bun
   bun run dev
   ```

   This will start both the Next.js UI (port 3000) and C# agent server (port 8000) concurrently.

## Available Scripts

The following scripts can also be run using your preferred package manager:

- `dev` - Starts both UI and agent servers in development mode
- `dev:debug` - Starts development servers with debug logging enabled
- `dev:ui` - Starts only the Next.js UI server
- `dev:agent` - Starts only the C# agent server
- `build` - Builds the Next.js application for production
- `start` - Starts the production server
- `lint` - Runs ESLint for code linting
- `install:agent` - Restores NuGet packages for the C# agent

## Project Structure

```
├── agent/                  # C# Agent (Microsoft Agent Framework)
│   ├── Program.cs         # Main agent implementation with tools
│   ├── ProverbsAgent.csproj  # .NET project file
│   └── Properties/        # Configuration (launch settings)
├── src/
│   ├── app/
│   │   ├── page.tsx      # Main UI with CopilotKit sidebar
│   │   ├── layout.tsx    # CopilotKit provider setup
│   │   └── api/
│   │       └── copilotkit/
│   │           └── route.ts  # AG-UI integration endpoint
│   ├── components/       # UI components (weather, proverbs, moon)
│   └── lib/             # Types and utilities
└── scripts/             # Helper scripts for agent setup/run
```

## Features Demonstrated

This starter showcases key AG-UI protocol features:

- **🔄 Shared State**: Proverbs list synchronized between frontend and agent
- **🎨 Generative UI**: Weather card rendered from backend tool
- **👤 Human-in-the-Loop**: Moon card with approval workflow
- **🛠️ Frontend Actions**: Theme color changes from agent
- **💬 Agentic Chat**: Natural language interface with tool calling

## 📚 Documentation

- [Microsoft Agent Framework](https://github.com/microsoft/agents) - Learn about Microsoft's agent framework
- [AG-UI Protocol](https://github.com/copilotkit/ag-ui) - AG-UI protocol specification
- [CopilotKit Documentation](https://docs.copilotkit.ai) - CopilotKit features and API
- [Next.js Documentation](https://nextjs.org/docs) - Next.js features and API
- [OpenAI Platform](https://platform.openai.com) - OpenAI API keys and models

## Contributing

Feel free to submit issues and enhancement requests! This starter is designed to be easily extensible.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Troubleshooting

### Agent Connection Issues

If you see "I'm having trouble connecting to my tools", make sure:

1. The C# agent is running on port 8000
2. Your GitHub token is set correctly via user secrets
3. Both servers started successfully (check terminal output)

### .NET SDK Not Installed

If you don't have .NET 9.0 installed:

**macOS/Linux (Homebrew):**

```bash
brew install dotnet@9
dotnet --version
```

**macOS/Linux (Install Script):**

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 9.0
export PATH="$HOME/.dotnet:$PATH"
```

**Windows (WinGet):**

```powershell
winget install --id=Microsoft.DotNet.SDK.9 -e
```

**Windows/macOS (Direct Download):**

- Visit https://dotnet.microsoft.com/download/dotnet/9.0
- Download and run the installer

### .NET SDK Issues

If you encounter .NET-related errors:

```bash
# Verify .NET SDK is installed
dotnet --version  # Should be 9.0.x or higher

# Restore packages manually
cd agent
dotnet restore
dotnet run
```

### OpenAI API Key Issues

If the agent fails to start with "OpenAI API key not found":

```bash
cd agent
dotnet user-secrets set "OpenAI:ApiKey" "<your-openai-api-key>"
```

Or set it via environment variable:

```bash
export OPENAI_API_KEY="<your-openai-api-key>"
```

### Port Conflicts

If port 8000 is already in use, you can change it in:

- `agent/Properties/launchSettings.json` - Update `applicationUrl`
- `src/app/api/copilotkit/route.ts` - Update the HttpAgent URL
