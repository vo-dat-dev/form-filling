const OLLAMA_BASE_URL = process.env.OLLAMA_BASE_URL ?? "http://localhost:11434";
const EMBEDDING_MODEL = process.env.EMBEDDING_MODEL ?? "bge-m3";

export async function generateEmbedding(text: string): Promise<number[] | null> {
  if (!text?.trim()) return null;

  try {
    const res = await fetch(`${OLLAMA_BASE_URL}/api/embed`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ model: EMBEDDING_MODEL, input: text.trim() }),
    });

    if (!res.ok) {
      console.warn(`Ollama embed failed: ${res.status} ${res.statusText}`);
      return null;
    }

    const data = await res.json();
    return data.embeddings?.[0] ?? null;
  } catch (error) {
    console.warn("Ollama embed error (is Ollama running?):", error);
    return null;
  }
}
