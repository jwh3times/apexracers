/**
 * The size bound on one Telemetry Upload, mirrored from `ApexRacers.Core.TelemetryUpload`
 * (`src/ApexRacers.Core/TelemetryUpload.cs`) — the authoritative copy. Change the two together.
 *
 * The mirror exists so the page can refuse an oversized file *before* sending it. The API enforces
 * the same bound regardless (a client check is a courtesy, never the control), but without this the
 * only way to learn a 300 MB file is too big is to upload all 300 MB of it first.
 */

/** The file bound in whole megabytes — the number the page advertises. */
export const MAX_UPLOAD_MEGABYTES = 250;

/** The file bound in bytes, as `File.size` reports it. */
export const MAX_UPLOAD_BYTES = MAX_UPLOAD_MEGABYTES * 1024 * 1024;

/**
 * Returns the refusal to show for a file of `sizeBytes`, or `null` when it is within the bound.
 *
 * Phrased to match what the API answers with a 413, so a file the page lets through and the server
 * then refuses does not contradict what the page would have said.
 */
export function tooLargeMessage(sizeBytes: number): string | null {
  return sizeBytes > MAX_UPLOAD_BYTES
    ? `Telemetry file is too large. The limit is ${MAX_UPLOAD_MEGABYTES} MB per file.`
    : null;
}
