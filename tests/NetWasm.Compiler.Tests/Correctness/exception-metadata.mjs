export var exceptionClauseSize = 12;
export var exceptionClauseValueOffset = 4;

export function readExceptionClause(view, metadata, index, plus, asNumber) {
  var address = plus(metadata, index * exceptionClauseSize);
  return {
    kind: view.getUint32(asNumber(address), true),
    value: view.getUint32(
      asNumber(plus(address, exceptionClauseValueOffset)),
      true),
  };
}
