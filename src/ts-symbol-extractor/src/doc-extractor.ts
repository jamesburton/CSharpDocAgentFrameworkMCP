import ts from 'typescript';
import type { DocComment } from './types.js';

/**
 * Extracts JSDoc/TSDoc documentation for a symbol.
 */
export function getDocComment(symbol: ts.Symbol, checker: ts.TypeChecker): DocComment | null {
  const summary = ts.displayPartsToString(symbol.getDocumentationComment(checker));
  const tags = symbol.getJsDocTags(checker);

  if (!summary && tags.length === 0) {
    return null;
  }

  const params: Record<string, string> = {};
  const throws: Record<string, string> = {};
  const see: string[] = [];
  const typeParams: Record<string, string> = {};
  let returns: string | null = null;
  let remarks: string | null = null;
  let example: string | null = null;

  for (const tag of tags) {
    const text = tag.text ? ts.displayPartsToString(tag.text) : '';
    
    switch (tag.name) {
      case 'param': {
        // Param tags usually have the parameter name as the first part of the text
        const match = text.match(/^(\S+)\s*(.*)/);
        if (match) {
          params[match[1]] = match[2];
        }
        break;
      }
      case 'template':
      case 'typeparam': {
        const match = text.match(/^(\S+)\s*(.*)/);
        if (match) {
          typeParams[match[1]] = match[2];
        }
        break;
      }
      case 'returns':
      case 'return':
        returns = text;
        break;
      case 'remarks':
        remarks = text;
        break;
      case 'example':
        example = text;
        break;
      case 'see':
        see.push(text);
        break;
      case 'throws':
      case 'exception': {
        const match = text.match(/^(\S+)\s*(.*)/);
        if (match) {
          throws[match[1]] = match[2];
        } else {
          throws['unknown'] = text;
        }
        break;
      }
    }
  }

  return {
    summary: summary || null,
    remarks,
    params,
    typeParams,
    returns,
    example,
    throws,
    see
  };
}
