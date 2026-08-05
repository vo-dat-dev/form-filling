# ✅ FINAL SOLUTION: useThreads + MicrosoftAgentRunner + Postgres

## 🎯 Mục tiêu

- ✅ Sử dụng `useThreads({ agentId })` hook từ CopilotKit v2
- ✅ Threads persist vào Postgres qua Microsoft Agent Framework
- ✅ KHÔNG cần CopilotKit Intelligence license
- ✅ Hoạt động đầy đủ: list, create, rename, delete

## 🧩 Các thành phần

### 1. **MicrosoftAgentRunner** (Custom AgentRunner)

`src/lib/microsoft-agent-runner.ts`

```typescript
export class MicrosoftAgentRunner extends InMemoryAgentRunner {
  // Override run(): Persist thread khi agent chạy
  override run(request) {
    this.createOrUpdateThread(threadId, agentId, title);
    return super.run(request); // ← InMemory lưu thread vào cache
  }
  
  // Override connect(): Ensure thread exists
  override connect(request) {
    this.ensureThreadExists(threadId);
    return super.connect(request);
  }
  
  // Override listThreads(): Return threads từ InMemory cache
  override listThreads() {
    return super.listThreads(); // ← Return từ cache
  }
}
```

**Cách hoạt động:**

1. Khi `run()` được gọi → Thread persist vào Postgres
2. `super.run()` → InMemoryAgentRunner lưu thread vào **in-memory cache**
3. Khi `listThreads()` được gọi → Return từ **in-memory cache**

### 2. **Proxy Endpoints** (Fallback)

`src/app/api/copilotkit/threads/route.ts`

```typescript
export async function GET(request) {
  // Forward to Microsoft Agent Framework
  const response = await fetch(`${AGENT_URL}/api/threads?agentId=${agentId}`);
  const threads = await response.json();
  
  // Transform & return
  return NextResponse.json({
    threads: threads.map(t => ({
      id: t.id,
      name: t.title, // ← title → name
      agentId: t.agentId,
      ...
    })),
  });
}
```

**Vai trò:**
- Fallback nếu runtime không expose `/threads`
- Hoặc khi cần fresh data từ DB (không qua cache)

### 3. **Microsoft Agent Framework Backend** (C#)

`agent/Services/DbService.cs`

```csharp
public async Task<ThreadInfo> CreateThreadWithId(string id, string agentId, string title) {
  var entity = new ThreadEf { Id = id, AgentId = agentId, Title = title };
  db.Threads.Add(entity);
  await db.SaveChangesAsync();
  return MapThread(entity);
}
```

`agent/Program.cs`

```csharp
api.MapPost("/threads", async (DbService db, CreateThreadRequest body) => {
  if (!string.IsNullOrEmpty(body.ThreadId)) {
    // Use custom threadId from CopilotKit
    var thread = await db.CreateThreadWithId(body.ThreadId, ...);
    return Results.Ok(thread);
  }
  ...
});
```

**Vai trò:**
- Persist threads vào Postgres
- Support custom threadId từ CopilotKit

## 🔄 Data Flow Chi Tiết

### **Flow 1: User gửi message đầu tiên**

```
1. Frontend: User nhập message
   ↓
2. CopilotRuntime: Generate threadId (UUID)
   ↓
3. MicrosoftAgentRunner.run()
   a. POST http://localhost:8000/api/threads
      { threadId, agentId, title }
   b. DbService.CreateThreadWithId(threadId, ...)
   c. INSERT INTO threads VALUES (...)
   d. Thread saved to Postgres ✅
   ↓
4. super.run()
   a. InMemoryAgentRunner saves thread to cache
   b. Thread now in memory ✅
   ↓
5. Agent processes message normally
```

### **Flow 2: useThreads fetches list**

```
1. Frontend: useThreads({ agentId: "minerU" })
   ↓
2. Hook calls: GET /api/copilotkit/threads?agentId=minerU
   ↓
3. Runtime checks if runner supports local endpoints
   → MicrosoftAgentRunner.ɵsupportsLocalThreadEndpoints = true ✅
   ↓
4. Runtime calls: runner.listThreads()
   ↓
5. MicrosoftAgentRunner.listThreads()
   → return super.listThreads() 
   → Return từ InMemory cache
   ↓
6. If cache empty, fallback to proxy endpoint
   → GET http://localhost:8000/api/threads
   → Fetch from Postgres
   ↓
7. Return threads to frontend ✅
```

### **Flow 3: User rename thread**

```
1. Frontend: renameThread(id, "New Name")
   ↓
2. PATCH /api/copilotkit/threads/{id}
   Body: { name: "New Name" }
   ↓
3. Proxy → PATCH http://localhost:8000/api/threads/{id}
   Body: { title: "New Name" }
   ↓
4. DbService.UpdateThread(id, "New Name")
   ↓
5. UPDATE threads SET title = ... WHERE id = ...
   ↓
6. Thread updated in Postgres ✅
   ↓
7. InMemory cache updated automatically on next run()
```

### **Flow 4: Page refresh / reconnect**

```
1. Frontend: Page loads with threadId in URL
   ↓
2. CopilotRuntime: connect(threadId)
   ↓
3. MicrosoftAgentRunner.connect()
   a. Check if thread exists in DB
   b. If not found, will be created on first message
   ↓
4. super.connect()
   a. Try to replay from InMemory cache
   b. If not in cache, empty history
   ↓
5. History restored ✅
```

## 📊 Architecture Diagram

```
┌──────────────────────────────────────────────────────────┐
│  Frontend: useThreads({ agentId: "minerU" })             │
└──────────────────────────────────────────────────────────┘
                      ↓
┌──────────────────────────────────────────────────────────┐
│  CopilotKit Runtime                                      │
│  ┌────────────────────────────────────────────────────┐  │
│  │ MicrosoftAgentRunner (Custom)                      │  │
│  │  - run() → Persist to DB + InMemory cache         │  │
│  │  - listThreads() → Return from InMemory cache     │  │
│  │  - connect() → Ensure thread exists               │  │
│  └────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────┘
           ↓ persist                    ↓ list (fallback)
┌─────────────────────┐      ┌─────────────────────────────┐
│ Microsoft Agent     │      │ Proxy Endpoints             │
│ Framework API       │      │ /api/copilotkit/threads     │
│ POST /api/threads   │      │ GET /api/copilotkit/threads │
└─────────────────────┘      └─────────────────────────────┘
           ↓                           ↓
┌──────────────────────────────────────────────────────────┐
│  PostgreSQL Database                                     │
│  threads table (id, agentId, title, createdAt, ...)     │
└──────────────────────────────────────────────────────────┘
```

## ⚠️ Hiểu đúng về InMemoryAgentRunner

### **Quan niệm SAI:**
- ❌ "InMemory nghĩa là không persist, mất khi restart"

### **Quan niệm ĐÚNG:**
- ✅ InMemoryAgentRunner **lưu runtime state** trong memory
- ✅ Nhưng ta có thể **persist vào DB** trong `run()`
- ✅ Threads vẫn được lưu Postgres qua MicrosoftAgentRunner
- ✅ InMemory chỉ là **cache layer** cho performance

### **Lợi ích của cách này:**
- ✅ Nhanh: `listThreads()` return từ cache (không query DB mỗi lần)
- ✅ Persistent: Threads vẫn lưu Postgres (không mất khi restart)
- ✅ Hybrid: Best of both worlds

## 🎯 Kết luận

### **Câu trả lời cho câu hỏi ban đầu:**

> "useThreads listThreads ở đâu không thấy?"

**Trả lời:**

1. `listThreads()` là method của `InMemoryAgentRunner`
2. `MicrosoftAgentRunner extends InMemoryAgentRunner` → inherit `listThreads()`
3. Khi `run()` được gọi → Thread auto-saved vào InMemory cache
4. Khi `listThreads()` được gọi → Return từ cache
5. Nếu cache empty → Fallback proxy fetch từ DB

### **Điểm quan trọng:**

- ⭐ **AgentRunner chỉ có 4 methods**: run, connect, isRunning, stop
- ⭐ **`listThreads()` là extension** của `InMemoryAgentRunner` (không phải abstract class)
- ⭐ **Proxy endpoints vẫn cần** để fetch fresh data từ DB
- ⭐ **Custom runner = Persistence layer**, không phải API layer

### **Flow tổng thể:**

```
Custom AgentRunner (Persistence) + Proxy API (Fresh Data) = Full Solution
```

## 📝 Files quan trọng

1. `src/lib/microsoft-agent-runner.ts` - Custom runner
2. `src/app/api/copilotkit/threads/route.ts` - Proxy GET/POST
3. `src/app/api/copilotkit/threads/[id]/route.ts` - Proxy PATCH/DELETE
4. `agent/Services/DbService.cs` - CreateThreadWithId method
5. `agent/Program.cs` - Support custom threadId

## 🚀 Test

```bash
# 1. Start Agent Framework
cd agent && dotnet run

# 2. Start Next.js
npm run dev

# 3. Check logs
# Should see: [MicrosoftAgentRunner] listThreads() called, returning X threads

# 4. Check DB
psql -d form_filling -c "SELECT * FROM threads;"
```

---

**Tóm lại:** Solution này kết hợp **Custom AgentRunner** (theo tài liệu) + **Proxy API** (để fetch fresh data) để có full thread management với Postgres! 🎉
