import ts from 'typescript';
import path from 'node:path';
import { SymbolKind, Accessibility, NodeKind, type SymbolGraphSnapshot, type SymbolNode, type SourceSpan, type SymbolEdge, SymbolEdgeKind, EdgeScope, type ParameterInfo, type GenericConstraint, type SymbolId } from './types.js';
import { getSymbolId } from './symbol-id.js';
import { getDocComment } from './doc-extractor.js';

/**
 * Extracts a SymbolGraphSnapshot from a TypeScript project defined by a tsconfig.json.
 */
export function extractSymbols(tsconfigPath: string): SymbolGraphSnapshot {
  const absoluteTsconfigPath = path.resolve(tsconfigPath).replace(/\\/g, '/');
  const projectRoot = path.dirname(absoluteTsconfigPath);
  const projectName = path.basename(projectRoot);

  // 1. Load tsconfig.json
  const configFile = ts.readConfigFile(absoluteTsconfigPath, ts.sys.readFile);
  if (configFile.error) {
    throw new Error(`Error reading tsconfig.json: ${ts.flattenDiagnosticMessageText(configFile.error.messageText, '\n')}`);
  }

  const parsedConfig = ts.parseJsonConfigFileContent(
    configFile.config,
    ts.sys,
    projectRoot,
    undefined,
    absoluteTsconfigPath
  );

  if (parsedConfig.errors.length > 0) {
    throw new Error(`Error parsing tsconfig.json: ${ts.flattenDiagnosticMessageText(parsedConfig.errors[0].messageText, '\n')}`);
  }

  // 2. Create program and get checker
  const program = ts.createProgram(parsedConfig.fileNames, parsedConfig.options);
  const checker = program.getTypeChecker();

  const nodes: SymbolNode[] = [];
  const edges: SymbolEdge[] = [];

  // 3. Walk project source files
  const projectRootNormalized = projectRoot.replace(/\\/g, '/').toLowerCase();

  for (const sourceFile of program.getSourceFiles()) {
    const fileName = sourceFile.fileName.replace(/\\/g, '/').toLowerCase();
    // Skip node_modules and ambient declarations (.d.ts)
    if (sourceFile.isDeclarationFile || fileName.includes('node_modules')) {
      continue;
    }

    // Only process files that are within the project root
    if (!fileName.startsWith(projectRootNormalized)) {
      continue;
    }

    const normalizedOriginalFileName = sourceFile.fileName.replace(/\\/g, '/');
    const relativePath = path.relative(projectRoot, normalizedOriginalFileName).replace(/\\/g, '/');
    const fileNodeId: SymbolId = { value: `N:${projectName}:${relativePath}:file` };

    // Map each file to a SymbolKind.Namespace node
    const fileNode: SymbolNode = {
      id: fileNodeId,
      kind: SymbolKind.Namespace,
      displayName: relativePath,
      fullyQualifiedName: relativePath,
      accessibility: Accessibility.Public,
      span: getSourceSpan(sourceFile, sourceFile, projectRoot),
      docComment: null,
      parameters: [],
      genericConstraints: [],
      nodeKind: NodeKind.Real
    };

    nodes.push(fileNode);

    // 4. Extract declarations within the file
    ts.forEachChild(sourceFile, (node) => {
      extractDeclaration(node, fileNodeId, nodes, edges, checker, projectRoot, projectName, sourceFile);
    });
  }

  return {
    schemaVersion: '1.0',
    projectName,
    sourceFingerprint: 'ts-compiler',
    contentHash: null,
    createdAt: new Date().toISOString(),
    nodes,
    edges,
    ingestionMetadata: null,
    solutionName: null,
  };
}

function extractDeclaration(
  node: ts.Node,
  parentId: SymbolId,
  nodes: SymbolNode[],
  edges: SymbolEdge[],
  checker: ts.TypeChecker,
  projectRoot: string,
  projectName: string,
  sourceFile: ts.SourceFile,
  knownSymbol?: ts.Symbol
) {
  const symbol = knownSymbol ?? ((node as any).symbol as ts.Symbol | undefined);
  if (!symbol) {
    // Some nodes might not have symbols but their children do (e.g. VariableStatement)
    if (ts.isVariableStatement(node)) {
      ts.forEachChild(node.declarationList, (child) => {
        extractDeclaration(child, parentId, nodes, edges, checker, projectRoot, projectName, sourceFile);
      });
    }
    return;
  }

  const kind = mapSymbolKind(symbol);
  if (kind === undefined) return;

  const id = getSymbolId(symbol, checker, projectRoot, projectName);
  
  // Avoid duplicate nodes (can happen with multiple declarations)
  if (nodes.some(n => n.id.value === id.value)) return;

  const nodeObj: SymbolNode = {
    id,
    kind,
    displayName: symbol.getName(),
    fullyQualifiedName: checker.getFullyQualifiedName(symbol),
    accessibility: getAccessibility(node),
    span: getSourceSpan(node, sourceFile, projectRoot),
    docComment: getDocComment(symbol, checker),
    parameters: getParameters(node, checker),
    genericConstraints: getGenericConstraints(node, checker),
    returnType: getReturnType(node, checker),
    nodeKind: NodeKind.Real
  };

  nodes.push(nodeObj);
  edges.push({
    sourceId: parentId,
    targetId: id,
    kind: SymbolEdgeKind.Contains,
    scope: EdgeScope.IntraProject
  });

  // Extract members (for classes and interfaces — instance members)
  if (symbol.members) {
    symbol.members.forEach((memberSymbol) => {
      const memberDecls = memberSymbol.getDeclarations();
      if (memberDecls) {
        for (const memberDecl of memberDecls) {
          extractDeclaration(memberDecl, id, nodes, edges, checker, projectRoot, projectName, sourceFile, memberSymbol);
        }
      }
    });
  }

  // Extract exports for enums (enum members live in symbol.exports, not symbol.members)
  if (symbol.exports && (symbol.flags & ts.SymbolFlags.Enum)) {
    symbol.exports.forEach((exportedSymbol) => {
      if (exportedSymbol.flags & ts.SymbolFlags.EnumMember) {
        const memberDecls = exportedSymbol.getDeclarations();
        if (memberDecls) {
          for (const memberDecl of memberDecls) {
            extractDeclaration(memberDecl, id, nodes, edges, checker, projectRoot, projectName, sourceFile, exportedSymbol);
          }
        }
      }
    });
  }

  // Handle class/interface inheritance
  if (ts.isClassDeclaration(node) || ts.isInterfaceDeclaration(node)) {
    const type = checker.getDeclaredTypeOfSymbol(symbol);
    const baseTypes = checker.getBaseTypes(type as ts.InterfaceType);
    for (const baseType of baseTypes) {
      const baseSymbol = baseType.getSymbol();
      if (baseSymbol) {
        const baseId = getSymbolId(baseSymbol, checker, projectRoot, projectName);
        edges.push({
          sourceId: id,
          targetId: baseId,
          kind: SymbolEdgeKind.Inherits,
          scope: EdgeScope.IntraProject
        });
      }
    }

    if (ts.isClassDeclaration(node) && node.heritageClauses) {
      for (const clause of node.heritageClauses) {
        if (clause.token === ts.SyntaxKind.ImplementsKeyword) {
          for (const typeNode of clause.types) {
            const t = checker.getTypeAtLocation(typeNode);
            const s = t.getSymbol();
            if (s) {
              const baseId = getSymbolId(s, checker, projectRoot, projectName);
              edges.push({
                sourceId: id,
                targetId: baseId,
                kind: SymbolEdgeKind.Implements,
                scope: EdgeScope.IntraProject
              });
            }
          }
        }
      }
    }
  }
}

function mapSymbolKind(symbol: ts.Symbol): SymbolKind | undefined {
  const flags = symbol.flags;

  if (flags & ts.SymbolFlags.Module) return SymbolKind.Namespace;
  if (flags & ts.SymbolFlags.Class) return SymbolKind.Type;
  if (flags & ts.SymbolFlags.Interface) return SymbolKind.Type;
  if (flags & ts.SymbolFlags.TypeAlias) return SymbolKind.Type;
  if (flags & ts.SymbolFlags.Enum) return SymbolKind.Type;
  if (flags & ts.SymbolFlags.EnumMember) return SymbolKind.EnumMember;
  if (flags & ts.SymbolFlags.Function) return SymbolKind.Method;
  if (flags & ts.SymbolFlags.Method) return SymbolKind.Method;
  if (flags & ts.SymbolFlags.Property) return SymbolKind.Property;
  if (flags & ts.SymbolFlags.Variable) return SymbolKind.Field;
  if (flags & ts.SymbolFlags.Constructor) return SymbolKind.Constructor;

  return undefined;
}

function getAccessibility(node: ts.Node): Accessibility {
  const modifiers = ts.canHaveModifiers(node) ? ts.getModifiers(node) : undefined;
  
  if (modifiers) {
    if (modifiers.some(m => m.kind === ts.SyntaxKind.PrivateKeyword)) return Accessibility.Private;
    if (modifiers.some(m => m.kind === ts.SyntaxKind.ProtectedKeyword)) return Accessibility.Protected;
  }

  // Check if it's exported
  const isExported = (ts.getCombinedModifierFlags(node as ts.Declaration) & ts.ModifierFlags.Export) !== 0;
  return isExported ? Accessibility.Public : Accessibility.Internal;
}

function getParameters(node: ts.Node, checker: ts.TypeChecker): ParameterInfo[] {
  if (!ts.isFunctionLike(node)) return [];

  return node.parameters.map(p => {
    const type = checker.getTypeAtLocation(p);
    return {
      name: p.name.getText(),
      typeName: checker.typeToString(type),
      isOptional: !!p.questionToken || !!p.initializer,
      defaultValue: p.initializer?.getText() || null
    };
  });
}

function getGenericConstraints(node: ts.Node, checker: ts.TypeChecker): GenericConstraint[] {
  if (ts.isClassDeclaration(node) || ts.isInterfaceDeclaration(node) || ts.isFunctionLike(node)) {
    return (node.typeParameters || []).map(tp => ({
      typeParameterName: tp.name.getText(),
      constraints: tp.constraint ? [checker.typeToString(checker.getTypeAtLocation(tp.constraint))] : []
    }));
  }
  return [];
}

function getReturnType(node: ts.Node, checker: ts.TypeChecker): string | undefined {
  if (ts.isFunctionLike(node)) {
    const signature = checker.getSignatureFromDeclaration(node);
    if (signature) {
      return checker.typeToString(signature.getReturnType());
    }
  }
  return undefined;
}

/**
 * Extracts SourceSpan from a TypeScript node.
 */
function getSourceSpan(node: ts.Node, sourceFile: ts.SourceFile, projectRoot: string): SourceSpan {
  const { line, character } = sourceFile.getLineAndCharacterOfPosition(node.getStart());
  const { line: endLine, character: endCharacter } = sourceFile.getLineAndCharacterOfPosition(node.getEnd());

  return {
    filePath: path.relative(projectRoot, sourceFile.fileName).replace(/\\/g, '/'),
    startLine: line + 1,
    startColumn: character + 1,
    endLine: endLine + 1,
    endColumn: endCharacter + 1,
  };
}
