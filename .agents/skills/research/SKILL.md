---
name: research
description: Investigate a question against high-trust primary sources and capture the findings as a Markdown file in the repo. Use when the user wants a topic researched, docs or API facts gathered, or reading legwork delegated to a background agent.
---

Spin up a **background agent** to do the research, so you keep working while it reads.

Its job:

1. Investigate the question against **primary sources** — official docs, source code, specs, first-party APIs — not a secondary write-up of them. Follow every claim back to the source that owns it.
2. Classify the result before choosing a path. Public-safe engineering research follows the public
   repository's existing convention. Maintainer-only findings, raw provider material, account or
   deployment detail, and non-public planning belong in the optional companion at `private/`.
3. Before assigning a private output, require `private/.git`. If the companion is absent, do not
   create an ignored orphan `private/` directory; report that the private result has nowhere portable
   to land and ask whether to install the companion or choose a public-safe destination.
4. Write the findings to a single Markdown file, citing each claim's source.
5. Save it where the selected repository already keeps such notes; match its existing convention,
   and if there is none, put it somewhere sensible and say where.
