using System.Security.Cryptography;
using System.Text;

namespace CSweet.Agent.SDK;

public static partial class DeliveryComputeTest
{
    public static string NetworkRestoreJobKey(string executionKey) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(executionKey)));
    public static string RestoreJobMarker(string jobKey,string status) => $"csweet-restore:{jobKey}:{status}";

    public static string StartNetworkRestoreScript(string root,string archiveHash,string lockfileHash,string jobKey)
    {
        var prefix=RestorePrefix(root,archiveHash,lockfileHash,jobKey);
        return ($"""
set -eu
cd {root}
if [ -f restore-{jobKey}.result ] || systemctl cat csweet-npm-{jobKey}.service >/dev/null 2>&1; then
  printf '%s\n' '{RestoreJobMarker(jobKey,"retained")}'
  exit 0
fi
{prefix}
cat > restore-{jobKey}.sh <<'CSWEET_INSTALL_JOB'
#!/bin/sh
cd {root}
docker run --rm --network=bridge -v "$PWD/source:/app" -w /app csweet/node:22 npm ci --no-audit --no-fund > restore-{jobKey}.log 2>&1
result=$?
printf '%s\n' "$result" > restore-{jobKey}.result.tmp
mv restore-{jobKey}.result.tmp restore-{jobKey}.result
exit "$result"
CSWEET_INSTALL_JOB
chmod 700 restore-{jobKey}.sh
systemd-run --no-block --unit=csweet-npm-{jobKey} --property=Type=oneshot --property=RemainAfterExit=yes --property=RuntimeMaxSec=infinity /bin/sh {root}/restore-{jobKey}.sh
""").Replace("\r\n","\n");
    }

    public static string ObserveNetworkRestoreScript(string root,string archiveHash,string lockfileHash,string jobKey)
    {
        _=RestorePrefix(root,archiveHash,lockfileHash,jobKey);
        return ($"""
set -eu
cd {root}
printf '%s  source.zip\n' '{archiveHash}' | sha256sum -c -
printf '%s  source/package-lock.json\n' '{lockfileHash}' | sha256sum -c -
if [ -f restore-{jobKey}.result ]; then
  result=$(cat restore-{jobKey}.result)
  tail -c 6000 restore-{jobKey}.log || true
  if [ "$result" = 0 ]; then printf '\n%s\n' '{RestoreJobMarker(jobKey,"ok")}'; exit 0; fi
  printf '\nDependency installation failed (exit %s).\n' "$result" >&2
  exit 1
fi
state=$(systemctl show csweet-npm-{jobKey}.service --property=ActiveState --value 2>/dev/null || true)
case "$state" in
  active|activating|reloading) tail -c 2000 restore-{jobKey}.log 2>/dev/null || true; printf '\n%s\n' '{RestoreJobMarker(jobKey,"running")}' ;;
  *) printf 'Dependency installation job is %s; inspect its retained log.\n' "$state" >&2; tail -c 6000 restore-{jobKey}.log 2>/dev/null || true; exit 1 ;;
esac
""").Replace("\r\n","\n");
    }

    private static string RestorePrefix(string root,string archiveHash,string lockfileHash,string jobKey)
    {
        if(jobKey.Length!=64||jobKey.Any(c=>!"0123456789abcdef".Contains(c)))throw new ArgumentException("Use a stable restore job identity.");
        var legacy=RestoreScript(root,archiveHash,lockfileHash);
        return legacy[..legacy.LastIndexOf("docker run --rm",StringComparison.Ordinal)];
    }
}
