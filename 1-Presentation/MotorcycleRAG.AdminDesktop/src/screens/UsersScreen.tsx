import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/apiClient";
import { PageHeader, Empty } from "@/components/ui";

interface ManagedUser {
  userId: string;
  email: string;
  tier?: string;
  enabled?: boolean;
}

export default function UsersScreen() {
  const users = useQuery({
    queryKey: ["users"],
    queryFn: async () => {
      const res = await api.get<{ items?: ManagedUser[] } | ManagedUser[]>("/api/admin/users");
      return Array.isArray(res.data) ? res.data : (res.data.items ?? []);
    },
  });

  return (
    <div>
      <PageHeader title="Users" subtitle="api/admin/users" />
      {users.isLoading ? (
        <Empty>Loading users…</Empty>
      ) : users.isError ? (
        <Empty>Could not load users: {String((users.error as Error).message)}</Empty>
      ) : (users.data ?? []).length === 0 ? (
        <Empty>No users found.</Empty>
      ) : (
        <div className="overflow-hidden rounded-xl border border-border">
          {(users.data ?? []).map((u) => (
            <div key={u.userId} className="flex items-center justify-between border-b border-border px-4 py-3 last:border-b-0">
              <div className="text-sm">{u.email}</div>
              <div className="flex items-center gap-3 text-xs text-muted">
                {u.tier && <span>{u.tier}</span>}
                <span>{u.enabled === false ? "Disabled" : "Enabled"}</span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
