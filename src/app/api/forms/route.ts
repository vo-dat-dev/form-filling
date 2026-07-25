import { NextRequest, NextResponse } from "next/server";
import { prisma } from "@/lib/prisma";
import type { FormConfig } from "@/lib/types";

export async function GET() {
  try {
    const forms = await prisma.form.findMany({
      orderBy: { updatedAt: "desc" },
      include: { _count: { select: { submissions: true } } },
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
    const form = await prisma.form.create({
      data: {
        title: body.title,
        description: body.description ?? null,
        fields: JSON.parse(JSON.stringify(body.fields)),
      },
    });
    return NextResponse.json(form, { status: 201 });
  } catch (error) {
    console.error("Failed to create form:", error);
    return NextResponse.json(
      { error: "Failed to create form" },
      { status: 500 }
    );
  }
}
