export function validateRuntimeComponentMetadata(sections, expectedMetadata) {
  if (sections.length !== 1) {
    throw new Error('Runtime must contain exactly one native component metadata section');
  }
  if (expectedMetadata.byteLength === 0 || !Buffer.from(sections[0]).equals(expectedMetadata)) {
    throw new Error('Runtime component metadata does not match the native WIT contract');
  }
}
