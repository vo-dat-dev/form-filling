#!/bin/bash

SCRIPT_DIR="$(dirname "$0")"

# Load .env from project root into environment
ENV_FILE="$SCRIPT_DIR/../.env"
if [ -f "$ENV_FILE" ]; then
  while IFS='=' read -r key value; do
    # Skip comments and blank lines
    [[ "$key" =~ ^#.*$ || -z "$key" ]] && continue
    export "$key=$value"
  done < "$ENV_FILE"
fi

# Navigate to the agent directory
cd "$SCRIPT_DIR/../agent" || exit 1

# Run the C# agent
echo "🚀 Starting C# Proverbs Agent on http://localhost:8000..."
echo ""
dotnet run --launch-profile http
