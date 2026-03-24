import { describe, it, expect, beforeAll } from 'vitest';
import path from 'node:path';
import fs from 'node:fs';
import { extractSymbols } from '../src/extractor.js';
import type { SymbolGraphSnapshot } from '../src/types.js';
import { SymbolKind } from '../src/types.js';

describe('extractor', () => {
  const fixturePath = path.resolve(__dirname, 'fixtures/simple-project/tsconfig.json');
  const goldenDir = path.resolve(__dirname, 'golden-files');

  if (!fs.existsSync(goldenDir)) {
    fs.mkdirSync(goldenDir, { recursive: true });
  }

  let snapshot: SymbolGraphSnapshot;

  beforeAll(() => {
    snapshot = extractSymbols(fixturePath);
  });

  it('should extract symbols from a simple project and match golden file', () => {
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
    const nodeModulesNode = snapshot.nodes.find(n => n.id.value.includes('node_modules'));
    expect(nodeModulesNode).toBeUndefined();
  });

  it('should map source files to Namespace nodes', () => {
    const indexFileNode = snapshot.nodes.find(n => n.kind === SymbolKind.Namespace && n.displayName === 'src/index.ts');

    expect(indexFileNode).toBeDefined();
    expect(indexFileNode?.id.value).toBe('N:simple-project:src/index.ts:file');
    expect(indexFileNode?.span.filePath).toContain('index.ts');
  });

  it('should extract enum type and enum members', () => {
    const directionNode = snapshot.nodes.find(n => n.displayName === 'Direction' && n.kind === SymbolKind.Type);
    expect(directionNode).toBeDefined();
    expect(directionNode!.id.value).toContain('Direction');

    const northMember = snapshot.nodes.find(n => n.displayName === 'North' && n.kind === SymbolKind.EnumMember);
    expect(northMember).toBeDefined();
  });

  it('should extract type alias', () => {
    const greetingNode = snapshot.nodes.find(n => n.displayName === 'Greeting' && n.kind === SymbolKind.Type);
    expect(greetingNode).toBeDefined();
    expect(greetingNode!.id.value).toContain('Greeting');
  });

  it('should extract constructor', () => {
    const ctorNode = snapshot.nodes.find(n => n.kind === SymbolKind.Constructor && n.id.value.includes('ConfiguredGreeter'));
    expect(ctorNode).toBeDefined();
  });

  it('should extract class field/property', () => {
    const prefixNode = snapshot.nodes.find(n => n.displayName === 'prefix' && n.id.value.includes('ConfiguredGreeter'));
    expect(prefixNode).toBeDefined();
    expect([SymbolKind.Property, SymbolKind.Field]).toContain(prefixNode!.kind);
  });
});
