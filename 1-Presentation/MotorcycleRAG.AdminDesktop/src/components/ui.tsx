import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export function Card({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <div className={cn("rounded-xl border border-border bg-card/60 p-4", className)}>{children}</div>
  );
}

export function MetricCard({ label, value, tone }: { label: string; value: ReactNode; tone?: "success" | "danger" | "default" }) {
  const toneClass =
    tone === "success" ? "text-success" : tone === "danger" ? "text-danger" : "text-foreground";
  return (
    <div className="rounded-lg bg-secondary/60 p-3">
      <div className="mb-1 text-xs text-muted">{label}</div>
      <div className={cn("text-lg font-medium", toneClass)}>{value}</div>
    </div>
  );
}

type Variant = "default" | "primary" | "danger";
export function Button({
  children,
  onClick,
  variant = "default",
  disabled,
  className,
  type = "button",
}: {
  children: ReactNode;
  onClick?: () => void;
  variant?: Variant;
  disabled?: boolean;
  className?: string;
  type?: "button" | "submit";
}) {
  const variantClass =
    variant === "primary"
      ? "bg-primary text-primary-foreground hover:bg-primary/90 border-transparent"
      : variant === "danger"
        ? "border-danger/60 text-danger hover:bg-danger/10"
        : "border-border text-foreground hover:bg-secondary";
  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm transition-colors disabled:cursor-not-allowed disabled:opacity-50",
        variantClass,
        className,
      )}
    >
      {children}
    </button>
  );
}

export function StatusPill({ ok, label }: { ok: boolean; label: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs",
        ok ? "bg-success/15 text-success" : "bg-secondary text-muted",
      )}
    >
      <span className={cn("h-1.5 w-1.5 rounded-full", ok ? "bg-success" : "bg-muted")} />
      {label}
    </span>
  );
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="mb-4 flex items-start justify-between gap-3">
      <div>
        <h1 className="text-xl font-medium">{title}</h1>
        {subtitle && <div className="mt-0.5 text-xs text-muted">{subtitle}</div>}
      </div>
      {actions && <div className="flex shrink-0 gap-2">{actions}</div>}
    </div>
  );
}

export function Empty({ icon, children }: { icon?: ReactNode; children: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 py-12 text-center text-sm text-muted">
      {icon}
      {children}
    </div>
  );
}
