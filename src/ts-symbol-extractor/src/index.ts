import { createInterface } from 'node:readline';
import fs from 'node:fs';
import { extractSymbols } from './extractor.js';
import type { ExtractRequest, ExtractResponse, ErrorResponse } from './types.js';

/**
 * Handles a single JSON-RPC request line and returns a JSON-RPC response line.
 */
export async function handleRequest(line: string): Promise<string | null> {
  let request: any;
  try {
    request = JSON.parse(line);
  } catch {
    const error: ErrorResponse = {
      jsonrpc: '2.0',
      id: null,
      error: { code: -32700, message: 'Parse error' },
    };
    return JSON.stringify(error);
  }

  const { jsonrpc, id, method, params } = request;

  if (jsonrpc !== '2.0') {
    const error: ErrorResponse = {
      jsonrpc: '2.0',
      id: id ?? null,
      error: { code: -32600, message: 'Invalid Request' },
    };
    return JSON.stringify(error);
  }

  if (method === 'extract') {
    try {
      const snapshot = extractSymbols(params.tsconfigPath);
      const response: ExtractResponse = {
        jsonrpc: '2.0',
        id,
        result: snapshot,
      };
      return JSON.stringify(response);
    } catch (err: any) {
      const error: ErrorResponse = {
        jsonrpc: '2.0',
        id,
        error: { code: -32603, message: err.message || 'Internal error' },
      };
      return JSON.stringify(error);
    }
  }

  const error: ErrorResponse = {
    jsonrpc: '2.0',
    id: id ?? null,
    error: { code: -32601, message: 'Method not found' },
  };
  return JSON.stringify(error);
}

/**
 * Entry point for the sidecar. Reads NDJSON from stdin and writes responses to stdout.
 */
async function main() {
  const rl = createInterface({
    input: process.stdin,
    terminal: false,
  });

  for await (const line of rl) {
    if (!line.trim()) continue;
    const response = await handleRequest(line);
    if (response) {
      // Check if we should write to a file or stdout
      const request = JSON.parse(line);
      const outputPath = request.params?.outputPath;

      if (outputPath) {
        fs.writeFileSync(outputPath, response + '\n');
        // Write a small success response to stdout to signal completion
        process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: request.id, result: { success: true, filePath: outputPath } }) + '\n');
      } else {
        const success = process.stdout.write(response + '\n');
        if (!success) {
          await new Promise((resolve) => process.stdout.once('drain', resolve));
        }
      }
    }
  }
  }


// Only run main if this file is the entry point
if (import.meta.url.endsWith(process.argv[1]) || process.argv[1].endsWith('index.ts') || process.argv[1].endsWith('index.js')) {
  main().catch((err) => {
    console.error('Fatal error in sidecar:', err);
    process.exit(1);
  });
}
