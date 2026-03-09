import { describe, it, expect } from 'vitest';
import path from 'node:path';
import { handleRequest } from '../src/index.js';

describe('IPC handler', () => {
  it('should return a valid JSON-RPC response for extract method', async () => {
    const fixturePath = path.resolve(__dirname, 'fixtures/simple-project/tsconfig.json');
    const request = JSON.stringify({
      jsonrpc: '2.0',
      id: 1,
      method: 'extract',
      params: { tsconfigPath: fixturePath }
    });

    const responseLine = await handleRequest(request);
    const response = JSON.parse(responseLine!);

    expect(response.jsonrpc).toBe('2.0');
    expect(response.id).toBe(1);
    expect(response.result).toBeDefined();
    expect(response.result.projectName).toBe('simple-project');
  });

  it('should return an error for invalid JSON', async () => {
    const request = 'invalid json';
    const responseLine = await handleRequest(request);
    const response = JSON.parse(responseLine!);

    expect(response.jsonrpc).toBe('2.0');
    expect(response.id).toBeNull();
    expect(response.error.code).toBe(-32700);
    expect(response.error.message).toBe('Parse error');
  });

  it('should return an error for unknown method', async () => {
    const request = JSON.stringify({
      jsonrpc: '2.0',
      id: 2,
      method: 'unknown',
      params: {}
    });

    const responseLine = await handleRequest(request);
    const response = JSON.parse(responseLine!);

    expect(response.jsonrpc).toBe('2.0');
    expect(response.id).toBe(2);
    expect(response.error.code).toBe(-32601);
    expect(response.error.message).toBe('Method not found');
  });
});
