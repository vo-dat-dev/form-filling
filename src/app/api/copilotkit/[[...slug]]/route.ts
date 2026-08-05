import {
  CopilotRuntime,
  CopilotKitIntelligence,
  createCopilotEndpoint,
  InMemoryAgentRunner,
} from "@copilotkit/runtime/v2";
import { HttpAgent } from "@ag-ui/client";
import { handle } from "hono/vercel";
import { MicrosoftAgentRunner } from "@/lib/microsoft-agent-runner";

const runtime = new CopilotRuntime({
  agents: {
    minerU: new HttpAgent({
      url: `${process.env.AGENT_URL || "http://localhost:8000"}/minerU`,
    }),
  },
  // Use MicrosoftAgentRunner for Postgres persistence

  // --- copilotkit:intelligence (optional - for advanced features) ---
  ...(process.env.COPILOTKIT_LICENSE_TOKEN
    ? {
      intelligence: new CopilotKitIntelligence({
        apiKey: process.env.INTELLIGENCE_API_KEY ?? "",
        apiUrl: process.env.INTELLIGENCE_API_URL ?? "http://localhost:4201",
        wsUrl:
          process.env.INTELLIGENCE_GATEWAY_WS_URL ?? "ws://localhost:4401",
      }),
      identifyUser: () => ({ id: "demo-user", name: "Demo User" }),
      licenseToken: process.env.COPILOTKIT_LICENSE_TOKEN,
    }
    : {
      runner: new MicrosoftAgentRunner(),
    }),
  // --- /copilotkit:intelligence ---
});

const app = createCopilotEndpoint({
  runtime,
  basePath: "/api/copilotkit",
});

export const GET = handle(app);
export const POST = handle(app);
export const PATCH = handle(app);
export const DELETE = handle(app);
