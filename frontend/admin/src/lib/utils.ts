export function cn(...classes: Array<string | false | null | undefined>) {
  return classes.filter(Boolean).join(" ");
}

export function formatDate(value?: string | null) {
  if (!value) return "-";
  return new Date(value).toLocaleString("es-MX", {
    hour12: false
  });
}
