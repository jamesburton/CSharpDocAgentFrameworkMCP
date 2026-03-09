import { describe, it, expect } from 'vitest';
import path from 'node:path';
import fs from 'node:fs';
import { extractSymbols } from '../src/extractor.js';
import { SymbolKind } from '../src/types.js';

describe('extractor', () => {
  const fixturePath = path.resolve(__dirname, 'fixtures/simple-project/tsconfig.json');
  const goldenDir = path.resolve(__dirname, 'golden-files');

  if (!fs.existsSync(goldenDir)) {
    fs.mkdirSync(goldenDir, { recursive: true });
  }

  it('should extract symbols from a simple project and match golden file', () => {
    const snapshot = extractSymbols(fixturePath);

    // Normalize createdAt for comparison
    const normalizedSnapshot = {
      ...snapshot,
      createdAt: '2026-03-08T00:00:00.000Z'
    };

    const goldenFile = path.join(goldenDir, 'simple-project.json');

    if (!fs.existsSync(goldenFile)) {
      // Bootstrap: create golden file
      fs.writeFileSync(goldenFile, JSON.stringify(normalizedSnapshot, null, 2));
      console.log(`Created golden file: ${goldenFile}`);
    } else {
      const goldenContent = JSON.parse(fs.readFileSync(goldenFile, 'utf-8'));
      expect(normalizedSnapshot).toEqual(goldenContent);
    }
  });

  it('should exclude node_modules', () => {
    const snapshot = extractSymbols(fixturePath);
    const nodeModulesNode = snapshot.nodes.find(n => n.id.includes('node_modules'));
    expect(nodeModulesNode).toBeUndefined();
  });

  it('should map source files to Namespace nodes', () => {
    const snapshot = extractSymbols(fixturePath);
    const indexFileNode = snapshot.nodes.find(n => n.kind === SymbolKind.Namespace && n.name === 'src/index.ts');
    
    expect(indexFileNode).toBeDefined();
    expect(indexFileNode?.id).toBe('N:simple-project:src/index.ts:file');
    expect(indexFileNode?.span.filePath).toContain('index.ts');
  });
});
