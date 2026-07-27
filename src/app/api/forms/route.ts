import { NextRequest, NextResponse } from "next/server";
import { prisma } from "@/lib/prisma";
import type { FormConfig } from "@/lib/types";
import { generateEmbedding } from "@/lib/embedding";

export async function GET(request: NextRequest) {
  try {
    const q = request.nextUrl.searchParams.get("q");

    if (q?.trim()) {
      const embedding = await generateEmbedding(q);
      if (!embedding) {
        return NextResponse.json({ error: "Failed to generate embedding for search" }, { status: 500 });
      }

      const vectorStr = `[${embedding.join(",")}]`;
      const rows = await prisma.$queryRawUnsafe<
        Array<Record<string, unknown>>
      >(
        `SELECT f.*, f.embedding::text AS embedding,
                1 - (f.embedding <=> $1::vector) AS similarity,
                (SELECT COUNT(*)::int FROM "FormSubmission" fs WHERE fs."formId" = f.id) AS submission_count
         FROM "Form" f
         WHERE f.embedding IS NOT NULL
         ORDER BY f.embedding <=> $1::vector
         LIMIT 10`,
        vectorStr,
      );

      const forms = rows.map((row) => {
        const { submission_count, embedding: embStr, similarity, ...rest } = row;
        return {
          ...rest,
          embedding: embStr ? JSON.parse(embStr as string) : null,
          similarity: similarity ? Number(similarity) : null,
          _count: { submissions: submission_count },
        };
      });

      return NextResponse.json(forms);
    }

    const rows = await prisma.$queryRawUnsafe<
      Array<Record<string, unknown>>
    >(
      `SELECT f.*,
              (SELECT COUNT(*)::int FROM "FormSubmission" fs WHERE fs."formId" = f.id) AS submission_count,
              f.embedding::text AS embedding
       FROM "Form" f
       ORDER BY f."updatedAt" DESC`,
    );

    const forms = rows.map((row) => {
      const { submission_count, embedding, ...rest } = row;
      return {
        ...rest,
        embedding: embedding ? JSON.parse(embedding as string) : null,
        _count: { submissions: submission_count },
      };
    });

    return NextResponse.json(forms);
  } catch (error) {
    console.error("Failed to fetch forms:", error);
    return NextResponse.json(
      { error: "Failed to fetch forms" },
      { status: 500 }
    );
  }
}

export async function POST(request: NextRequest) {
  try {
    const body = (await request.json()) as FormConfig;
    if (!body.title || !body.fields?.length) {
      return NextResponse.json(
        { error: "Title and at least one field are required" },
        { status: 400 }
      );
    }

    const embedding = body.description
      ? await generateEmbedding(body.description)
      : null;

    const form = await prisma.form.create({
      data: {
        title: body.title,
        description: body.description ?? null,
        fields: JSON.parse(JSON.stringify(body.fields)),
      },
    });

    if (embedding) {
      await prisma.$executeRawUnsafe(
        `UPDATE "Form" SET embedding = $1::vector WHERE id = $2`,
        `[${embedding.join(",")}]`,
        form.id,
      );
    }

    return NextResponse.json({ ...form, embedding }, { status: 201 });
  } catch (error) {
    console.error("Failed to create form:", error);
    return NextResponse.json(
      { error: "Failed to create form" },
      { status: 500 }
    );
  }
}
