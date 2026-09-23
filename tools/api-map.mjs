#!/usr/bin/env node
// A compact map of the API, read from the committed OpenAPI document.
//
// The document is the single source of the API's shape (CLAUDE.md invariant 11),
// and at ~89 KB it is too large to read whole every time a question is about one
// endpoint. This prints one line per operation, or one schema with the names of
// the schemas it references, so a reader — a person or a coding agent — asks the
// document instead of opening the slices or the generated types.
//
//   node tools/api-map.mjs                  every operation
//   node tools/api-map.mjs cart             operations whose path, tag or id contains "cart"
//   node tools/api-map.mjs --schema Cart    one schema, and what it references
//   node tools/api-map.mjs --schemas        every schema name
//
// No dependencies: it must run on a fresh clone before anything is restored.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const documentPath = join(dirname(fileURLToPath(import.meta.url)), '..', 'docs', 'openapi', 'tendero.json');
const document = JSON.parse(readFileSync(documentPath, 'utf8'));
const schemas = document.components?.schemas ?? {};

const refName = (ref) => ref.split('/').pop();

// Renders a schema as a short type expression: `string`, `Money[]`, `Record<string, string>`.
function typeOf(schema) {
  if (!schema) return 'unknown';
  if (schema.$ref) return refName(schema.$ref);
  if (schema.enum) return schema.enum.map((v) => JSON.stringify(v)).join(' | ');
  const variants = schema.oneOf ?? schema.anyOf;
  if (variants) {
    const nonNull = variants.filter((v) => v.type !== 'null');
    const rendered = nonNull.map(typeOf).join(' | ');
    return nonNull.length < variants.length ? `${rendered} | null` : rendered;
  }
  if (schema.allOf) return schema.allOf.map(typeOf).join(' & ');

  const types = Array.isArray(schema.type) ? schema.type : [schema.type ?? 'object'];
  const nullable = types.includes('null') || schema.nullable === true;
  const rendered = types
    .filter((t) => t !== 'null')
    .map((t) => {
      if (t === 'array') return `${typeOf(schema.items)}[]`;
      if (t === 'object' && schema.additionalProperties && !schema.properties) {
        return `Record<string, ${typeOf(schema.additionalProperties === true ? undefined : schema.additionalProperties)}>`;
      }
      if (t === 'object' && schema.properties) return '{…}';
      return schema.format ? `${t}:${schema.format}` : t;
    })
    .join(' | ');
  return nullable ? `${rendered} | null` : rendered;
}

function describeOperation(method, path, operation) {
  const parameters = (operation.parameters ?? [])
    .filter((p) => p.in !== 'header')
    .map((p) => `${p.in === 'path' ? '{' : ''}${p.name}${p.in === 'path' ? '}' : ''}${p.required ? '' : '?'}:${typeOf(p.schema)}`);

  const body = operation.requestBody?.content?.['application/json']?.schema;
  if (body) parameters.push(`body:${typeOf(body)}`);

  const responses = Object.entries(operation.responses ?? {}).map(([status, response]) => {
    const schema = Object.values(response.content ?? {})[0]?.schema;
    return schema ? `${status} ${typeOf(schema)}` : status;
  });

  return `${method.toUpperCase().padEnd(6)} ${path}  ${operation.operationId ?? ''}  ${parameters.join(' ')}  → ${responses.join(' | ')}`;
}

function describeSchema(name) {
  const schema = schemas[name];
  if (!schema) {
    const close = Object.keys(schemas).filter((n) => n.toLowerCase().includes(name.toLowerCase()));
    console.error(`No schema named ${name}.${close.length ? ` Did you mean: ${close.join(', ')}` : ''}`);
    process.exit(1);
  }

  const required = new Set(schema.required ?? []);
  const lines = Object.entries(schema.properties ?? {}).map(
    ([property, value]) => `  ${property}${required.has(property) ? '' : '?'}: ${typeOf(value)}`,
  );
  console.log(lines.length ? `${name} {\n${lines.join('\n')}\n}` : `${name} = ${typeOf(schema)}`);

  const referenced = [...new Set([...JSON.stringify(schema).matchAll(/"\$ref":"([^"]+)"/g)].map((m) => refName(m[1])))];
  if (referenced.length) console.log(`references: ${referenced.join(', ')}`);
}

const args = process.argv.slice(2);

if (args[0] === '--schema') {
  if (!args[1]) {
    console.error('Usage: node tools/api-map.mjs --schema <Name>');
    process.exit(1);
  }
  describeSchema(args[1]);
} else if (args[0] === '--schemas') {
  console.log(Object.keys(schemas).sort().join('\n'));
} else {
  const filter = args[0]?.toLowerCase();
  for (const [path, item] of Object.entries(document.paths ?? {})) {
    for (const [method, operation] of Object.entries(item)) {
      if (!['get', 'post', 'put', 'patch', 'delete'].includes(method)) continue;
      const haystack = [path, operation.operationId, ...(operation.tags ?? [])].join(' ').toLowerCase();
      if (filter && !haystack.includes(filter)) continue;
      console.log(describeOperation(method, path, operation));
    }
  }
}
