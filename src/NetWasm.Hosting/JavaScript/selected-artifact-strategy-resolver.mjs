export function createSelectedArtifactStrategyResolver(expectedKind, execute) {
  return kind => {
    if (kind !== expectedKind) throw new TypeError("deployment kind is unsupported");
    return execute;
  };
}
