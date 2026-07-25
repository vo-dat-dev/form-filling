"use client";

import {
  CopilotChatConfigurationProvider,
  CopilotSidebar,
} from "@copilotkit/react-core/v2";
import Link from "next/link";

export default function MinerUPage() {
  return (
    <CopilotChatConfigurationProvider agentId="minerU">
      <div className="h-screen flex flex-col bg-slate-50">
        <header className="flex items-center gap-4 px-6 py-3 bg-white border-b border-slate-200 shadow-sm">
          <Link
            href="/"
            className="text-slate-500 hover:text-slate-800 text-sm transition-colors"
          >
            ← Back
          </Link>
          <h1 className="text-lg font-semibold text-slate-800">
            MinerU Document Assistant
          </h1>
          <span className="text-xs text-slate-400 ml-auto">
            Upload PDF, Word, Excel, PowerPoint, or images
          </span>
        </header>

        <main className="flex-1 flex items-center justify-center relative">
          <div className="text-center text-slate-400 select-none">
            <p className="text-5xl mb-4">📄</p>
            <p className="text-base font-medium text-slate-500">
              Upload a document and ask questions about it
            </p>
            <p className="text-sm mt-1">
              Supported: PDF · DOCX · PPTX · XLSX · PNG · JPG · WEBP
            </p>
          </div>

          <CopilotSidebar
            defaultOpen={true}
            labels={{
              modalHeaderTitle: "MinerU Assistant",
              welcomeMessageText:
                "👋 Upload a document (PDF, Word, image…) and I'll extract its content so you can ask questions about it.",
            }}
            attachments={{ enabled: true }}
          />
        </main>
      </div>
    </CopilotChatConfigurationProvider>
  );
}
