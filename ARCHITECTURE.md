# 🏗️ Architecture: CopilotKit + Microsoft Agent Framework

## 📊 Tổng quan hệ thống

Project này tích hợp **CopilotKit v2** (frontend) với **Microsoft Agent Framework** (backend) để quản lý threads/conversations với Postgres persistence.

### ⚡ Key Innovation: Custom AgentRunner

Thay vì dùng `InMemoryAgentRunner` (mất data khi restart) hoặc `IntelligenceAgentRunner` (cần license), chúng ta tạo **`MicrosoftAgentRunner`** - một custom runner kết nối CopilotKit với Microsoft Agent Framework's Postgres backend.

```
┌─────────────────────────────────────────────────────────────────┐
│              CopilotKit Runtime (Node.js)                        │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ MicrosoftAgentRunner (Custom)                             │ │
│  │  - Extends InMemoryAgentRunner                            │ │
│  │  - Override run(): Persist thread on agent start         │ │
│  │  - Override connect(): Ensure thread exists              │ │
│  │  - Forwards to Microsoft Agent Framework API             │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│                    Frontend (Next.js + React)                    │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ ThreadsDrawer Component                                    │ │
│  │  - Uses useThreads() hook from CopilotKit v2              │ │
│  │  - Renders conversation list                              │ │
│  │  - Handles thread selection, rename, delete               │ │
│  └────────────────────────────────────────────────────────────┘ │
│                            ↓                                     │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ useThreads({ agentId: "minerU" })                         │ │
│  │  - Calls: GET /api/copilotkit/threads?agentId=minerU     │ │
│  │  - Returns: { threads, isLoading, renameThread, delete } │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│              Next.js API Routes (Proxy Layer)                    │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ /api/copilotkit/threads/route.ts                          │ │
│  │  - GET: List threads → Forward to Agent Framework        │ │
│  │  - POST: Create thread → Forward to Agent Framework      │ │
│  │  - Transforms: title ↔ name                              │ │
│  └────────────────────────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ /api/copilotkit/threads/[id]/route.ts                    │ │
│  │  - PATCH: Rename thread → Forward to Agent Framework     │ │
│  │  - DELETE: Delete thread → Forward to Agent Framework    │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│         Microsoft Agent Framework (C# / ASP.NET Core)            │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ Program.cs - API Endpoints                                │ │
│  │  - GET  /api/threads?agentId=X                           │ │
│  │  - POST /api/threads                                      │ │
│  │  - PATCH /api/threads/{id}                               │ │
│  │  - DELETE /api/threads/{id}                              │ │
│  └────────────────────────────────────────────────────────────┘ │
│                            ↓                                     │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ DbService.cs                                              │ │
│  │  - ListThreads(agentId)                                   │ │
│  │  - CreateThread(agentId, title)                          │ │
│  │  - UpdateThread(id, title, metadata)                     │ │
│  │  - DeleteThread(id)                                       │ │
│  └────────────────────────────────────────────────────────────┘ │
│                            ↓                                     │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ FormFillingDbContext (Entity Framework)                   │ │
│  │  - Threads DbSet                                          │ │
│  │  - PostgreSQL + Pgvector                                  │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│                    PostgreSQL Database                           │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ threads table                                             │ │
│  │  - id (uuid, PK)                                          │ │
│  │  - agentId (text)                                         │ │
│  │  - title (text)                                           │ │
│  │  - metadata (text)                                        │ │
│  │  - createdAt (timestamp)                                  │ │
│  │  - updatedAt (timestamp)                                  │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

## 🔄 Data Flow

### 1. **List Threads (Page Load)**

```
User opens page
  ↓
useThreads({ agentId: "minerU" })
  ↓
GET /api/copilotkit/threads?agentId=minerU
  ↓
Next.js Proxy forwards to:
GET http://localhost:8000/api/threads?agentId=minerU
  ↓
DbService.ListThreads("minerU")
  ↓
SELECT * FROM threads WHERE "agentId" = 'minerU'
  ↓
Return List<ThreadInfo>
  ↓
Transform: title → name
  ↓
useThreads returns { threads, isLoading: false }
  ↓
ThreadsDrawer renders list
```

### 2. **Create New Thread**

```
User clicks "New Conversation"
  ↓
config.startNewThread()
  ↓
New threadId generated (UUID)
  ↓
User sends first message
  ↓
Agent run creates thread automatically
  ↓
Thread saved to Postgres
```

### 3. **Rename Thread**

```
User edits thread name
  ↓
renameThread(id, "New Name")
  ↓
PATCH /api/copilotkit/threads/{id}
Body: { name: "New Name" }
  ↓
Proxy forwards to:
PATCH http://localhost:8000/api/threads/{id}
Body: { title: "New Name" }
  ↓
DbService.UpdateThread(id, "New Name", null)
  ↓
UPDATE threads SET title = $1, "updatedAt" = NOW() WHERE id = $2
  ↓
Return updated ThreadInfo
  ↓
Transform: title → name
  ↓
UI updates
```

### 4. **Delete Thread**

```
User clicks delete button
  ↓
deleteThread(id)
  ↓
DELETE /api/copilotkit/threads/{id}
  ↓
Proxy forwards to:
DELETE http://localhost:8000/api/threads/{id}
  ↓
DbService.DeleteThread(id)
  ↓
DELETE FROM threads WHERE id = $1
  ↓
Return { success: true }
  ↓
Thread removed from UI
```

## 🔑 Key Components

### **Frontend (React/Next.js)**

| Component | Purpose |
|-----------|---------|
| `ThreadsDrawer.tsx` | UI component, uses `useThreads` hook |
| `useThreads` | CopilotKit v2 hook for thread management |
| `useCopilotChatConfiguration` | Controls active thread in chat |

### **Proxy Layer (Next.js API Routes)**

| Route | Purpose |
|-------|---------|
| `/api/copilotkit/threads` | Proxy GET/POST to Agent Framework |
| `/api/copilotkit/threads/[id]` | Proxy PATCH/DELETE to Agent Framework |

**Why Proxy?**
- CopilotKit `useThreads` expects `/api/copilotkit/threads`
- Microsoft Agent Framework serves `/api/threads`
- Proxy bridges the gap + transforms field names

### **Backend (C# Agent Framework)**

| Component | Purpose |
|-----------|---------|
| `Program.cs` | Defines `/api/threads/*` endpoints |
| `DbService.cs` | Business logic for thread CRUD |
| `FormFillingDbContext` | Entity Framework DB context |
| `ThreadEf` | Entity model for threads table |

### **Database (PostgreSQL)**

```sql
CREATE TABLE threads (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "agentId" TEXT NOT NULL,
    title TEXT NOT NULL,
    metadata TEXT,
    "createdAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "updatedAt" TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_threads_agentId ON threads("agentId");
```

## 🎯 Field Name Mapping

| CopilotKit (Frontend) | Microsoft Agent Framework (Backend) |
|----------------------|-------------------------------------|
| `name` | `title` |
| `agentId` | `agentId` |
| `createdAt` | `createdAt` |
| `updatedAt` | `updatedAt` |
| `archived` | *(not stored, always false)* |

## ✅ Benefits of This Architecture

1. **✅ Uses CopilotKit's `useThreads` hook** - Standard API, no custom logic
2. **✅ Microsoft Agent Framework persistence** - Battle-tested C# backend
3. **✅ PostgreSQL storage** - Durable, scalable, production-ready
4. **✅ Clean separation** - Frontend, proxy, backend well-defined
5. **✅ Easy to extend** - Add archive, metadata, search later

## 🚀 Running the System

### Start Database
```bash
docker-compose up -d postgres
```

### Start Agent Framework
```bash
cd agent
dotnet run
# Runs on http://localhost:8000
```

### Start Next.js
```bash
npm run dev
# Runs on http://localhost:3000
```

### Verify
- Open http://localhost:3000
- ThreadsDrawer should load threads from Postgres
- Create, rename, delete should work

## 🔍 Debugging

### Check if threads are stored:
```bash
psql -h localhost -U postgres -d form_filling
SELECT * FROM threads;
```

### Check Agent Framework API:
```bash
curl http://localhost:8000/api/threads?agentId=minerU
```

### Check Next.js Proxy:
```bash
curl http://localhost:3000/api/copilotkit/threads?agentId=minerU
```

### Check Frontend:
Open DevTools → Network → Filter "threads"

## 📝 Summary

**Architecture Pattern:** Frontend Hook → Proxy Layer → Agent Framework → Database

**Key Insight:** 
- CopilotKit provides the **UI/UX layer** with `useThreads`
- Microsoft Agent Framework provides the **persistence layer** with Postgres
- Next.js API routes act as a **bridge/adapter** between them

**Result:** 
- ✅ Best of both worlds
- ✅ No custom thread logic in frontend
- ✅ Production-ready backend
- ✅ Full Postgres persistence
