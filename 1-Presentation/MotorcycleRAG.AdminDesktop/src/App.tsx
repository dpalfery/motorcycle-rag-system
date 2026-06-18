import { Navigate, Route, Routes } from "react-router-dom";
import { useAuth } from "@/lib/auth";
import AppShell from "./components/AppShell";
import SignInScreen from "./screens/SignInScreen";
import ProcessorScreen from "./screens/ProcessorScreen";
import IngestionScreen from "./screens/IngestionScreen";
import JobsScreen from "./screens/JobsScreen";
import WebSourcesScreen from "./screens/WebSourcesScreen";
import McpToolsScreen from "./screens/McpToolsScreen";
import UsersScreen from "./screens/UsersScreen";
import SettingsScreen from "./screens/SettingsScreen";

export default function App() {
  const { signedIn } = useAuth();

  if (!signedIn) {
    return <SignInScreen />;
  }

  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<Navigate to="/processor" replace />} />
        <Route path="/processor" element={<ProcessorScreen />} />
        <Route path="/ingestion" element={<IngestionScreen />} />
        <Route path="/jobs" element={<JobsScreen />} />
        <Route path="/web-sources" element={<WebSourcesScreen />} />
        <Route path="/mcp-tools" element={<McpToolsScreen />} />
        <Route path="/users" element={<UsersScreen />} />
        <Route path="/settings" element={<SettingsScreen />} />
        <Route path="*" element={<Navigate to="/processor" replace />} />
      </Route>
    </Routes>
  );
}
