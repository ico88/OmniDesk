const NETWORK_FILTER_VERSION = [0, 8, 12] as const;

export function reportsFilteredNetworkAdapters(agentVersion: string | null | undefined): boolean {
  if (!agentVersion || !/^\d+\.\d+\.\d+$/.test(agentVersion)) return false;
  const version = agentVersion.split('.').map(Number);
  for (let index = 0; index < NETWORK_FILTER_VERSION.length; index++) {
    const current = version[index] ?? 0;
    const required = NETWORK_FILTER_VERSION[index] ?? 0;
    if (current !== required) return current > required;
  }
  return true;
}
