// A seed attribute's value as the catalogue says it, for a Shopify metafield
// that a storefront filter lists (phase 16). The seed holds the words a
// supplier wrote; the catalogue resolves each one against the attribute's
// definition (seed/attributes.sample.json, ADR 0056). A filter that listed the
// raw words would offer "sí" beside "si", so the script writes what the
// catalogue would show:
//   - an option: its Spanish label, matched by its code or any of its labels,
//     ignoring case, as AttributeDefinition.ResolveOption does;
//   - a yes/no: "sí" or "no", as AttributeDefinition.Accept reads them;
//   - anything else: the word as it came.
// The importer resolves the label to the same option, so the round trip holds.
import fs from 'node:fs';

const definitions = JSON.parse(fs.readFileSync('seed/attributes.sample.json', 'utf8'));
const list = Array.isArray(definitions) ? definitions : definitions.attributes;

const same = (a, b) => a.trim().toLowerCase() === b.trim().toLowerCase();

function definitionOf(key) {
  return list.find((definition) => same(definition.code, key) || (definition.aliases ?? []).some((alias) => same(alias, key)));
}

export function cleanValue(key, raw) {
  const value = String(raw).trim();
  const definition = definitionOf(key);
  if (!definition) return value;

  if (definition.kind === 'Option') {
    const code = value.toUpperCase().replace(/[\s-]+/g, '_');
    const option = (definition.options ?? []).find(
      (candidate) => same(candidate.code, code) || Object.values(candidate.label ?? {}).some((label) => same(label, value)),
    );
    return option?.label?.es ?? value;
  }

  if (definition.kind === 'Boolean') {
    if (['si', 'sí', 'yes', 'true', '1'].includes(value.toLowerCase())) return 'sí';
    if (['no', 'false', '0'].includes(value.toLowerCase())) return 'no';
  }

  return value;
}
