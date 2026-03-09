import ts from 'typescript';
import path from 'node:path';
import type { SymbolId } from './types.js';

/**
 * Generates a deterministic SymbolId for a TypeScript symbol.
 * Format: [Prefix]:[ProjectName]:[RelativePath]:[SymbolPath]
 * 
 * Prefixes (Roslyn style):
 * - N: Namespace (File)
 * - T: Type (Class, Interface, Enum, TypeAlias)
 * - M: Method (Function, Method, Constructor)
 * - P: Property
 * - F: Field (Variable, EnumMember)
 */
export function getSymbolId(
  symbol: ts.Symbol,
  checker: ts.TypeChecker,
  projectRoot: string,
  projectName: string
): SymbolId {
  const declarations = symbol.getDeclarations();
  if (!declarations || declarations.length === 0) {
    return { value: `T:${projectName}:unknown:${symbol.getName()}` };
  }

  const decl = declarations[0];
  const sourceFile = decl.getSourceFile();
  const absolutePath = sourceFile.fileName;
  const relativePath = path.relative(projectRoot, absolutePath).replace(/\\/g, '/');

  const prefix = getPrefix(symbol, decl);
  const symbolPath = getSymbolPath(symbol, checker);
  
  return { value: `${prefix}:${projectName}:${relativePath}:${symbolPath}` };
}

function getPrefix(symbol: ts.Symbol, decl: ts.Node): string {
  const flags = symbol.flags;

  if (flags & ts.SymbolFlags.Module) return 'N';
  if (flags & (ts.SymbolFlags.Class | ts.SymbolFlags.Interface | ts.SymbolFlags.TypeAlias | ts.SymbolFlags.Enum)) return 'T';
  if (flags & (ts.SymbolFlags.Function | ts.SymbolFlags.Method | ts.SymbolFlags.Constructor)) return 'M';
  if (flags & ts.SymbolFlags.Property) return 'P';
  if (flags & (ts.SymbolFlags.Variable | ts.SymbolFlags.EnumMember)) return 'F';

  return 'X';
}

/**
 * Recursively builds a dot-separated path for a symbol.
 */
function getSymbolPath(symbol: ts.Symbol, checker: ts.TypeChecker): string {
  const parts: string[] = [];
  let current: ts.Symbol | undefined = symbol;

  while (current) {
    let name = current.getName();

    if (name.startsWith('__')) {
      const decl = current.getDeclarations()?.[0];
      if (decl && ts.isConstructorDeclaration(decl)) {
        name = 'constructor';
      }
    }

    parts.unshift(name);

    const decl = current.valueDeclaration || current.getDeclarations()?.[0];
    if (decl && decl.parent) {
      let parentNode: ts.Node | undefined = decl.parent;
      let parentSymbol: ts.Symbol | undefined;

      while (parentNode) {
        if ((parentNode as any).symbol) {
          parentSymbol = (parentNode as any).symbol;
          break;
        }
        parentNode = parentNode.parent;
      }
      
      if (parentSymbol && !(parentSymbol.flags & ts.SymbolFlags.ValueModule)) {
        current = parentSymbol;
      } else {
        current = undefined;
      }
    } else {
      current = undefined;
    }
  }

  return parts.join('.');
}
