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

export enum NodeKind {
  Real = "Real",
  Stub = "Stub"
}

export enum EdgeScope {
  IntraProject = "IntraProject",
  CrossProject = "CrossProject",
  External = "External"
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

export enum SymbolKind {
  Namespace = "Namespace",
  Type = "Type",
  Method = "Method",
  Property = "Property",
  Field = "Field",
  Event = "Event",
  Parameter = "Parameter",
  Constructor = "Constructor",
  Delegate = "Delegate",
  Indexer = "Indexer",
  Operator = "Operator",
  Destructor = "Destructor",
  EnumMember = "EnumMember",
  TypeParameter = "TypeParameter"
}

export enum Accessibility {
  Public = "Public",
  Internal = "Internal",
  Protected = "Protected",
  Private = "Private",
  ProtectedInternal = "ProtectedInternal",
  PrivateProtected = "PrivateProtected"
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
  typeParameterName: string;
  constraints: string[];
}

export enum SymbolEdgeKind {
  Contains = "Contains",
  Inherits = "Inherits",
  Implements = "Implements",
  References = "References",
  Calls = "Calls",
  Overrides = "Overrides",
  Returns = "Returns",
  Invokes = "Invokes",
  Configures = "Configures",
  DependsOn = "DependsOn",
  Triggers = "Triggers",
  Imports = "Imports"
}

export interface IngestionMetadata {
  ingestedAt: string;
  ingestorVersion: string;
  additionalInfo: Record<string, string>;
}
