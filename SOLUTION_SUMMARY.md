# ✅ Solution Summary: CopilotKit useThreads + Microsoft Agent Framework

## 🎯 Vấn đề ban đầu

Bạn muốn:
- ✅ Sử dụng `useThreads` hook từ CopilotKit v2
- ✅ Lưu threads vào Postgres (không dùng InMemory)
- ✅ KHÔNG cần CopilotKit Intelligence license
- ✅ Tận dụng Microsoft Agent Framework đã có sẵn

## 💡 Giải pháp

Tạo **Custom AgentRunner** để bridge CopilotKit với Microsoft Agent Framework:

```typescript
// src/lib/microsoft-agent-runner.ts
export class MicrosoftAgentRunner extends InMemoryAgentRunner {
  override run(request) {
    // Persist thread to Microsoft Agent Framework
    this.createOrUpdateThread(threadId, agentId, title);
    return super.run(request);
  }
  
  override connect(request) {
    // Ensure thread exists
    this.ensureThreadExists(threadId);
    return super.connect(request);
  }
}
```

## 🏗️ Kiến trúc

```
useThreads({ agentId })
    ↓
CopilotRuntime.run(threadId)
    ↓
MicrosoftAgentRunner.run()
    ↓
POST http://localhost:8000/api/threads
Body: { threadId, agentId, title }
    ↓
DbService.CreateThreadWithId()
    ↓
INSERT INTO threads (id, agentId, title) VALUES (...)
    ↓
Thread persisted to Postgres ✅
```

## 📁 Files thay đổi

### 1. **Frontend (Next.js)**

#### `src/components/threads-drawer.tsx`
```typescript
import { useThreads } from "@copilotkit/react-core/v2";

const { threads, isLoading, renameThread, deleteThread } 
  = useThreads({ agentId: "minerU" });
```
- Sử dụng `useThreads` hook trực tiếp
- Không cần custom fetch logic

#### `src/lib/microsoft-agent-runner.ts` ⭐ (NEW)
```typescript
export class MicrosoftAgentRunner extends InMemoryAgentRunner {
  // Override run() để persist threads
  // Override connect() để ensure threads exist
}
```
- Custom AgentRunner
- Forwards to Microsoft Agent Framework API

#### `src/app/api/copilotkit/[[...slug]]/route.ts`
```typescript
import { MicrosoftAgentRunner } from "@/lib/microsoft-agent-runner";

const runtime = new CopilotRuntime({
  agents: { ... },
  runner: new MicrosoftAgentRunner(), // ⭐ Use custom runner
});
```
- Sử dụng custom runner thay vì InMemoryAgentRunner

### 2. **Proxy Layer (Next.js API)**

#### `src/app/api/copilotkit/threads/route.ts` ⭐ (NEW)
- Proxy `GET /threads` → Microsoft Agent Framework
- Proxy `POST /threads` → Microsoft Agent Framework
- Transform `title ↔ name`

#### `src/app/api/copilotkit/threads/[id]/route.ts` ⭐ (NEW)
- Proxy `PATCH /threads/:id` → Microsoft Agent Framework
- Proxy `DELETE /threads/:id` → Microsoft Agent Framework

**Why Proxy?**
- `useThreads` gọi `/api/copilotkit/threads`
- Microsoft Agent Framework serve `/api/threads`
- Proxy giúp transform & forward requests

### 3. **Backend (Microsoft Agent Framework C#)**

#### `agent/Services/DbService.cs`
```csharp
public async Task<ThreadInfo> CreateThreadWithId(string id, ...) {
  var entity = new ThreadEf { Id = id, ... }; // ⭐ Custom ID
  db.Threads.Add(entity);
  await db.SaveChangesAsync();
}
```
- Thêm method mới để support custom threadId

#### `agent/Program.cs`
```csharp
api.MapPost("/threads", async (DbService db, CreateThreadRequest body) => {
  if (!string.IsNullOrEmpty(body.ThreadId)) {
    // Use threadId from CopilotKit
    var thread = await db.CreateThreadWithId(body.ThreadId, ...);
  }
});

public record CreateThreadRequest(
  string? ThreadId, // ⭐ NEW field
  string? AgentId, 
  string? Title
);
```
- Support nhận `threadId` từ request body
- Upsert pattern: update nếu exists, create nếu không

## 🔄 Data Flow

### Khi user gửi message đầu tiên:

```
1. Frontend generate threadId (UUID)
2. useAgent sends message
3. CopilotRuntime calls MicrosoftAgentRunner.run()
4. MicrosoftAgentRunner → POST /api/threads { threadId, agentId, title }
5. DbService.CreateThreadWithId(threadId, ...)
6. INSERT INTO threads (id, agentId, title) VALUES (...)
7. Thread saved to Postgres ✅
8. Conversation continues...
```

### Khi user refresh page:

```
1. Frontend loads với threadId từ URL
2. CopilotRuntime calls MicrosoftAgentRunner.connect()
3. MicrosoftAgentRunner checks if thread exists
4. Thread found in Postgres ✅
5. History replays from InMemory cache
```

### Khi user list threads (useThreads):

```
1. useThreads({ agentId: "minerU" })
2. GET /api/copilotkit/threads?agentId=minerU
3. Proxy → GET http://localhost:8000/api/threads?agentId=minerU
4. DbService.ListThreads("minerU")
5. SELECT * FROM threads WHERE agentId = 'minerU'
6. Return threads array
7. Transform title → name
8. ThreadsDrawer renders list ✅
```

## ✅ Lợi ích

| Feature | InMemoryAgentRunner | MicrosoftAgentRunner |
|---------|-------------------|---------------------|
| **Persistence** | ❌ Mất khi restart | ✅ Lưu Postgres |
| **Multi-instance** | ❌ Không share | ⚠️ Partial (cần sync) |
| **Cost** | ✅ Free | ✅ Free |
| **useThreads works** | ⚠️ No list API | ✅ Full support |
| **Production-ready** | ❌ No | ✅ Yes |
| **Setup complexity** | ✅ Simple | ⚠️ Medium |

## 🚀 Cách chạy

### 1. Start Database
```bash
docker-compose up -d postgres
```

### 2. Start Microsoft Agent Framework
```bash
cd agent
dotnet run
# Runs on http://localhost:8000
```

### 3. Start Next.js
```bash
npm run dev:ui
# Runs on http://localhost:3000
```

### 4. Test
- Mở http://localhost:3000
- ThreadsDrawer hiển thị conversations từ Postgres
- Gửi message → Thread tự động persist
- Refresh page → History restore
- Rename/delete → Update Postgres

## 🔍 Debug

### Check if runner is working:
```bash
# Check Next.js logs
# Should see: [MicrosoftAgentRunner] Created thread xxx
```

### Check if threads are stored:
```bash
psql -h localhost -U postgres -d form_filling
SELECT * FROM threads;
```

### Check Agent Framework API:
```bash
curl http://localhost:8000/api/threads?agentId=minerU
```

### Check useThreads hook:
- Open DevTools → Network
- Filter "threads"
- Should see: GET /api/copilotkit/threads?agentId=minerU

## 📝 Key Insights

1. **AgentRunner is the key** 
   - Không phải là API proxy
   - Là nơi CopilotKit persist threads
   - Override `run()` và `connect()`

2. **ThreadId sync is critical**
   - CopilotKit generates threadId
   - Must pass to Microsoft Agent Framework
   - Use `CreateThreadWithId` với custom ID

3. **Dual API endpoints**
   - `/api/copilotkit/threads` cho `useThreads` hook
   - `/api/threads` cho Microsoft Agent Framework
   - Proxy bridges them

4. **Best of both worlds**
   - CopilotKit UI/UX (`useThreads` hook)
   - Microsoft Agent Framework persistence
   - No license needed
   - Production-ready

## 🎉 Kết luận

Bây giờ bạn có:
- ✅ `useThreads` hook hoạt động đầy đủ
- ✅ Threads persist vào Postgres
- ✅ Microsoft Agent Framework làm backend
- ✅ Không cần CopilotKit Intelligence license
- ✅ Production-ready architecture

**Architecture Pattern:** 
`useThreads` → `Custom AgentRunner` → `Microsoft Agent Framework` → `Postgres`

Đọc `ARCHITECTURE.md` để hiểu chi tiết! 📖
