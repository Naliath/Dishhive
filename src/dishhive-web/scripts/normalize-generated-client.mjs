import fs from 'node:fs';

// NSwag emits trailing spaces in optional-parameter documentation. Keep generation
// byte-reproducible and compatible with the repository's whitespace checks.
const file = 'src/app/api/generated/dishhive-api.client.ts';
const source = fs.readFileSync(file, 'utf8');
fs.writeFileSync(file, source.replace(/[\t ]+$/gm, ''));
