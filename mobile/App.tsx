import { StatusBar } from "expo-status-bar";
import { AuthProvider } from "./src/context/AuthContext";
import { PreferencesProvider } from "./src/context/PreferencesContext";
import { AppNavigator } from "./src/navigation/AppNavigator";

export default function App() {
  return (
    <PreferencesProvider>
      <AuthProvider>
        <StatusBar style="light" />
        <AppNavigator />
      </AuthProvider>
    </PreferencesProvider>
  );
}
