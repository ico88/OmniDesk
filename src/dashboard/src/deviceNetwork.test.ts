import { describe, expect, it } from 'vitest';
import { reportsFilteredNetworkAdapters } from './deviceNetwork';

describe('network adapter reporting version', () => {
  it.each([
    ['0.8.11', false],
    ['0.8.12', true],
    ['0.9.0', true],
    ['1.0.0', true],
    [null, false],
    ['unknown', false],
  ])('classifies Agent %s as filtered=%s', (version, expected) => {
    expect(reportsFilteredNetworkAdapters(version)).toBe(expected);
  });
});
