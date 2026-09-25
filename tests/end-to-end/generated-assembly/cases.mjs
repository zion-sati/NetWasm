const bindingLibraries = ['Microsoft.Extensions.Configuration', 'Microsoft.Extensions.Configuration.Abstractions',
  'Microsoft.Extensions.Primitives', 'Microsoft.Extensions.DependencyInjection.Abstractions', 'System.Linq'];

export const cases = [
  {
    id: 'stj', folder: 'stj-source-generation', assembly: 'NetWasm.Stj.SourceGeneration.Fixture',
    type: 'NetWasm.Tests.StjSourceGeneration.Fixture.EntryPoint', values: [0, 1, 17], expected: [1000, 1001, 1017],
    libraries: ['System.Text.Json', 'System.Memory', 'System.Text.Encodings.Web', 'System.IO.Pipelines'],
    aliases: { 'System.Buffers': 'System.Memory' }, desktop: 'oracle',
  },
  {
    id: 'regex', folder: 'regex-source-generation', assembly: 'NetWasm.Regex.SourceGeneration.Fixture',
    type: 'NetWasm.Tests.RegexSourceGeneration.Fixture.EntryPoint', values: [0, 1, 2, 3, 4, 5, 6, 7],
    expected: [101, 201, 301, 401, 501, 551, 601, 701], libraries: ['System.Text.RegularExpressions'], desktop: 'oracle',
  },
  {
    id: 'logging', folder: 'generated-assembly/fixtures/logging', assembly: 'Logging.Generated',
    type: 'NetWasm.GeneratorChecks.Logging.EntryPoint', values: [0, 1, 42, -1], expected: [42, 42, 42, 42],
    libraries: ['Microsoft.Extensions.Logging.Abstractions', 'Microsoft.Extensions.DependencyInjection.Abstractions'],
    analyzerPackages: ['Microsoft.Extensions.Logging.Abstractions'], desktop: 'oracle',
    desktopReferences: ['Microsoft.Extensions.Logging.Abstractions.dll'],
  },
  {
    id: 'activation', folder: 'generated-assembly/fixtures/activation', assembly: 'Activation.Generated',
    type: 'NetWasm.GeneratorChecks.Activation.EntryPoint', values: [0, 1, 2, 3, 4, 5, 6],
    expected: [42, 42, 42, 42, 42, 42, 42], libraries: bindingLibraries, desktop: 'stdout', contracts: true,
    analyzerPackages: ['Microsoft.Extensions.DependencyInjection', 'Microsoft.Extensions.Configuration.Binder'],
  },
  {
    id: 'binding', folder: 'generated-assembly/fixtures/binding', assembly: 'Binding.Generated',
    type: 'NetWasm.GeneratorChecks.Binding.EntryPoint', values: [0, 1, 2], expected: [42, 42, 42],
    libraries: bindingLibraries, desktop: 'stdout', contracts: true,
    analyzerPackages: ['Microsoft.Extensions.Configuration.Binder'],
  },
];

export function matchesObservations(observations, expected, desktop = false) {
  return Array.isArray(observations) && observations.length === expected.length &&
    observations.every((item, index) => desktop
      ? item.Kind === 'value' && item.Value === expected[index]
      : item.kind === 'value' && item.value === expected[index]);
}
