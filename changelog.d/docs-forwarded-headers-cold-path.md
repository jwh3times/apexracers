### Changed

- The forwarded-header trust documentation now records how a request that wakes the API from zero
  replicas is attributed. The platform delivers it through an internal hop, so it is logged and
  rate-limited as that hop rather than as its client. A forged `X-Forwarded-For` is still ignored on
  that path, and the HTTPS scheme still survives it. The deployment guidance now requires the
  forged-address check to include a request sent while the app is at zero replicas. No behavior
  changes.
