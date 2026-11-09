# Presenting KotobaReader

## A two-minute demonstration

1. Launch the app, click Demo, and explain that this is a native Windows reader.
2. Drag over the first Japanese bubble; show its recognized, editable text.
3. Click Translate offline. Disconnecting networking after setup does not break it.
4. Translate again to show the cache, save a note, and bookmark the page.
5. Navigate, zoom, enter focus mode, then reopen the book to show saved progress.
6. Show the Windows CI workflow and an integration-test run rather than claiming
   unmeasured OCR accuracy or performance.

## Resume wording

> Built a native Windows manga reader in C#/.NET and WPF with local Japanese OCR,
> CPU neural translation through a Python worker, persistent bookmarks and notes,
> and model-aware translation caching.

> Implemented cancellable asynchronous page loading and inference, bounded CBZ
> streaming, atomic save recovery, and automated core, worker, and Windows UI checks.

Only use claims you can explain and reproduce. Do not claim to have trained the
translation model or invented OCR. If adding metrics, measure startup latency,
warm translation latency, peak memory, and accuracy on a documented fixture set.

## Interview topics worth understanding

- Why isolate native ML work in a process, and why kill it after cancellation?
- How does the UI prevent an older page request from replacing a newer one?
- How do image coordinates survive zoom and window resizing?
- What can a malicious or malformed ZIP do, and what limits exist here?
- Why use a JSON document today, and when would SQLite become the better choice?
- How do model and pipeline changes invalidate cached translations?
- What do the tests prove, and what requires a representative manga evaluation?

## Follow-up work

Potential next improvements include a manga-specific OCR evaluation, thumbnail
index, continuous webtoon reading, full-resolution crop recovery, editable name
glossaries, speech-bubble detection, two-page spreads, an installer, and clean-machine
release testing. These are not claimed as implemented features.
