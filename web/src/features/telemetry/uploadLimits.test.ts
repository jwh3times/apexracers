import { describe, it, expect } from 'vitest';
import { MAX_UPLOAD_BYTES, MAX_UPLOAD_MEGABYTES, tooLargeMessage } from './uploadLimits';

describe('tooLargeMessage', () => {
  it('accepts a file at exactly the bound', () => {
    // The bound is inclusive on both sides of the wire: the API refuses only what is *over* it,
    // so a page that refused the boundary case would reject a file the server would have taken.
    expect(tooLargeMessage(MAX_UPLOAD_BYTES)).toBeNull();
  });

  it('accepts an ordinary file', () => {
    expect(tooLargeMessage(18 * 1024 * 1024)).toBeNull();
  });

  it('accepts an empty file', () => {
    // Emptiness is the API's to reject, with its own message. Not this bound's business.
    expect(tooLargeMessage(0)).toBeNull();
  });

  it('refuses a file one byte over the bound', () => {
    expect(tooLargeMessage(MAX_UPLOAD_BYTES + 1)).toMatch(/too large/i);
  });

  it('names the advertised limit in the refusal', () => {
    expect(tooLargeMessage(MAX_UPLOAD_BYTES + 1)).toContain(`${MAX_UPLOAD_MEGABYTES} MB`);
  });
});
