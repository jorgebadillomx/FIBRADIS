import { useState } from "react";
import { SettingsForm } from "../components/SettingsForm";
import { useSettings, useSettingsMutation } from "../hooks/useAdminApi";
import { SettingsResponse } from "../lib/api";

export function SettingsPage() {
  const { data, isLoading } = useSettings();
  const mutation = useSettingsMutation();
  const [feedback, setFeedback] = useState<string | null>(null);

  const handleSubmit = async (values: Partial<SettingsResponse>) => {
    await mutation.mutateAsync(values ?? {});
    setFeedback("Settings actualizados ✔");
  };

  return (
    <div className="space-y-4">
      <SettingsForm
        settings={data ?? undefined}
        loading={isLoading || mutation.isLoading}
        onSubmit={handleSubmit}
        statusMessage={feedback}
      />
    </div>
  );
}
