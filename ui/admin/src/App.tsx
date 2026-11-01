import { Card } from '@manoir-app/core-admin-ui-kit';
import '@manoir-app/core-admin-ui-kit/styles.css';
import './app.css';

export function App() {
  return (
    <main className="mn-kit-theme admin">
      <Card className="admin-card">
        <p>Manuel de la maison</p>
        <h1>Administration</h1>
        <p>Le back-office sera disponible dans une prochaine étape.</p>
      </Card>
    </main>
  );
}
