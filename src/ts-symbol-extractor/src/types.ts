export interface ExtractRequest {
  jsonrpc: '2.0';
  id: number;
  method: 'extract';
  params: {
    tsconfigPath: string;
    outputPath?: string;
  };
}

export interface ExtractResponse {
  jsonrpc: '2.0';
  id: number;
  result: SymbolGraphSnapshot;
}

export interface ErrorResponse {
  jsonrpc: '2.0';
  id: number | null;
  error: {
    code: number;
    message: string;
  };
}

export interface SymbolId {
  value: string;
}

export interface SymbolGraphSnapshot {
  schemaVersion: string;
  projectName: string;
  sourceFingerprint: string;
  contentHash: string | null;
  createdAt: string; // ISO timestamp
  nodes: SymbolNode[];
  edges: SymbolEdge[];
  ingestionMetadata: IngestionMetadata | null;
  solutionName: string | null;
}

export const enum NodeKind {
  Real = 0,
  Stub = 1
}

export const enum EdgeScope {
  IntraProject = 0,
  CrossProject = 1,
  External = 2
}

export interface SymbolNode {
  id: SymbolId;
  kind: SymbolKind;
  displayName: string;
  fullyQualifiedName: string;
  accessibility: Accessibility;
  span: SourceSpan;
  docComment: DocComment | null;
  parameters: ParameterInfo[];
  genericConstraints: GenericConstraint[];
  returnType?: string;
  nodeKind?: NodeKind;
}

export interface SymbolEdge {
  sourceId: SymbolId;
  targetId: SymbolId;
  kind: SymbolEdgeKind;
  scope?: EdgeScope;
}

export const enum SymbolKind {
  Namespace = 0,
  Type = 1,
  Method = 2,
  Property = 3,
  Field = 4,
  Event = 5,
  Parameter = 6,
  Constructor = 7,
  Delegate = 8,
  Indexer = 9,
  Operator = 10,
  Destructor = 11,
  EnumMember = 12,
  TypeParameter = 13
}

export const enum Accessibility {
  Public = 0,
  Internal = 1,
  Protected = 2,
  Private = 3,
  ProtectedInternal = 4,
  PrivateProtected = 5
}

export interface SourceSpan {
  filePath: string;
  startLine: number;
  startColumn: number;
  endLine: number;
  endColumn: number;
}

export interface DocComment {
  summary: string | null;
  params: Record<string, string>;
  typeParams: Record<string, string>;
  returns: string | null;
  example: string | null;
  throws: Record<string, string>;
  see: string[];
  remarks: string | null;
}

export interface ParameterInfo {
  name: string;
  typeName: string;
  isOptional: boolean;
  defaultValue: string | null;
}

export interface GenericConstraint {
  name: string;
  constraints: string[];
}

export const enum SymbolEdgeKind {
  Contains = 0,
  Extends = 1,
  Implements = 2,
  References = 3,
  Calls = 4,
  Overrides = 5,
  Inherits = 6
}

export interface IngestionMetadata {
  ingestedAt: string;
  ingestorVersion: string;
  additionalInfo: Record<string, string>;
}
