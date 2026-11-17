// Builds plugins.json, the index WritingIDE's plugin list reads, from this repository's GitHub discussions.
//
// A discussion is a plugin listing when all of this holds:
//   1. It has the "plugin" label. Only people with triage rights can add labels.
//   2. Its body has a ```plugin block of "key: value" lines (see the wiki: Publishing a plugin).
//   3. The download it names is approved: the discussion was written by a maintainer of this repository, or a
//      maintainer commented "approve <sha256>" with the checksum in the block. An author who later changes the
//      download or its checksum is no longer approved, so an approval covers exactly one file.
//
// Usage: GITHUB_TOKEN=... GITHUB_REPOSITORY=owner/name node build-plugin-index.js > plugins.json

const [owner, name] = (process.env.GITHUB_REPOSITORY || '').split('/');
const token = process.env.GITHUB_TOKEN;
if (!owner || !name || !token) {
  console.error('Set GITHUB_REPOSITORY (owner/name) and GITHUB_TOKEN.');
  process.exit(1);
}

const MAINTAINERS = new Set(['OWNER', 'MEMBER', 'COLLABORATOR']);
const LABEL = 'plugin';

async function graphql(query, variables) {
  const response = await fetch('https://api.github.com/graphql', {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', 'User-Agent': 'WritingIDE-plugin-index' },
    body: JSON.stringify({ query, variables }),
  });
  const result = await response.json();
  if (!response.ok || result.errors) {
    throw new Error(`GitHub API: ${JSON.stringify(result.errors || result)}`);
  }
  return result.data;
}

async function allDiscussions() {
  const query = `
    query ($owner: String!, $name: String!, $after: String) {
      repository(owner: $owner, name: $name) {
        discussions(first: 50, after: $after, orderBy: { field: CREATED_AT, direction: ASC }) {
          pageInfo { hasNextPage endCursor }
          nodes {
            number title url body authorAssociation
            author { login }
            labels(first: 20) { nodes { name } }
            comments(first: 100) { nodes { body authorAssociation } }
          }
        }
      }
    }`;
  const discussions = [];
  let after = null;
  do {
    const page = (await graphql(query, { owner, name, after })).repository.discussions;
    discussions.push(...page.nodes);
    after = page.pageInfo.hasNextPage ? page.pageInfo.endCursor : null;
  } while (after);
  return discussions;
}

/** The "key: value" lines of the first ```plugin block, with lower-case keys; null when there is none. */
function readBlock(body) {
  const match = /```plugin[^\n]*\n([\s\S]*?)```/i.exec(body || '');
  if (!match) {
    return null;
  }
  const fields = {};
  for (const line of match[1].split(/\r?\n/)) {
    const colon = line.indexOf(':');
    if (colon > 0) {
      fields[line.slice(0, colon).trim().toLowerCase()] = line.slice(colon + 1).trim();
    }
  }
  return fields;
}

function isHttps(url) {
  try {
    return new URL(url).protocol === 'https:';
  } catch {
    return false;
  }
}

function toListing(discussion) {
  const skip = (reason) => {
    console.error(`#${discussion.number} "${discussion.title}" is not listed: ${reason}`);
    return null;
  };
  const fields = readBlock(discussion.body);
  if (!fields) {
    return skip('no ```plugin block.');
  }
  const sha256 = (fields.sha256 || '').toLowerCase();
  if (!/^[A-Za-z0-9][A-Za-z0-9._-]{2,99}$/.test(fields.id || '')) {
    return skip('"id" is missing or is not a plain name such as com.example.myplugin.');
  }
  if (!/^v?\d+(\.\d+){0,3}$/.test(fields.version || '')) {
    return skip('"version" is missing or is not a number such as 1.0.0.');
  }
  if (!isHttps(fields.download || '')) {
    return skip('"download" is missing or is not an https link.');
  }
  if (!/^[0-9a-f]{64}$/.test(sha256)) {
    return skip('"sha256" is missing or is not 64 hex characters.');
  }
  const approved = MAINTAINERS.has(discussion.authorAssociation)
    || discussion.comments.nodes.some((comment) => MAINTAINERS.has(comment.authorAssociation)
      && new RegExp(`\\bapproved?\\b[^\\n]*${sha256}`, 'i').test(comment.body || ''));
  if (!approved) {
    return skip(`waiting for a maintainer to comment "approve ${sha256}".`);
  }
  return {
    id: fields.id,
    name: (fields.name || discussion.title).slice(0, 80),
    description: (fields.description || '').slice(0, 600),
    author: (fields.author || (discussion.author && discussion.author.login) || '').slice(0, 80),
    version: fields.version.replace(/^v/i, ''),
    sdk: /^\d+(\.\d+){0,3}$/.test(fields.sdk || '') ? fields.sdk : '1.0',
    download: fields.download,
    sha256,
    homepage: isHttps(fields.homepage || '') ? fields.homepage : '',
    discussion: discussion.url,
  };
}

(async () => {
  const discussions = await allDiscussions();
  const plugins = discussions
    .filter((discussion) => discussion.labels.nodes.some((label) => label.name.toLowerCase() === LABEL))
    .map(toListing)
    .filter(Boolean)
    .sort((a, b) => a.name.localeCompare(b.name, 'en', { sensitivity: 'base' }));
  console.error(`${plugins.length} plugin(s) listed.`);
  // No timestamp: the file only changes when a listing does, so the index branch has one commit per real change.
  process.stdout.write(JSON.stringify({ source: `https://github.com/${owner}/${name}/discussions`, plugins }, null, 2) + '\n');
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
