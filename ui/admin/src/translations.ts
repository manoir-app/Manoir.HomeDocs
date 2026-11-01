export type Language = 'fr' | 'en';

const translations = {
  fr: {
    app: 'Manuel de la maison',
    admin: 'Administration',
    hero: 'Un endroit pour prendre soin de la maison.',
    heroDescription:
      'Gérez le manuel de votre maison depuis votre réseau local.',
    setupTitle: 'Créer le compte administrateur',
    setupDescription:
      'Ce compte permettra de gérer le manuel. Il ne peut être créé qu’une seule fois.',
    loginTitle: 'Connexion',
    loginDescription: 'Connectez-vous pour accéder à l’administration.',
    userName: 'Nom d’utilisateur',
    password: 'Mot de passe',
    passwordHint: 'Au moins 12 caractères.',
    confirmation: 'Confirmer le mot de passe',
    create: 'Créer le compte',
    signIn: 'Se connecter',
    signingIn: 'Traitement en cours…',
    signOut: 'Se déconnecter',
    signedIn: 'Compte connecté',
    ready: 'Votre espace d’administration est prêt.',
    loading: 'Chargement de l’administration…',
    retry: 'Réessayer',
    language: 'Langue',
    invalid: 'Identifiant ou mot de passe incorrect.',
    invalidInput:
      'Données invalides ou vérification de sécurité expirée. Réessayez.',
    conflict: 'Un compte administrateur existe déjà. Connectez-vous.',
    throttled: 'Trop de tentatives. Réessayez dans une minute.',
    unavailable:
      'Le service est indisponible ou la demande a échoué. Réessayez.',
    passwordMismatch: 'Les mots de passe ne correspondent pas.',
    setupComplete: 'Compte créé. Vous pouvez maintenant vous connecter.',
    sessionExpired: 'Votre session a expiré. Connectez-vous à nouveau.',
  },
  en: {
    app: 'Home manual',
    admin: 'Administration',
    hero: 'A place to take care of your home.',
    heroDescription: 'Manage your home manual from your local network.',
    setupTitle: 'Create the administrator account',
    setupDescription:
      'This account manages the manual. It can only be created once.',
    loginTitle: 'Sign in',
    loginDescription: 'Sign in to access administration.',
    userName: 'Username',
    password: 'Password',
    passwordHint: 'At least 12 characters.',
    confirmation: 'Confirm password',
    create: 'Create account',
    signIn: 'Sign in',
    signingIn: 'Working…',
    signOut: 'Sign out',
    signedIn: 'Signed in',
    ready: 'Your administration area is ready.',
    loading: 'Loading administration…',
    retry: 'Try again',
    language: 'Language',
    invalid: 'Incorrect username or password.',
    invalidInput: 'Invalid input or expired security check. Try again.',
    conflict: 'An administrator account already exists. Sign in.',
    throttled: 'Too many attempts. Try again in one minute.',
    unavailable: 'The service is unavailable or the request failed. Try again.',
    passwordMismatch: 'The passwords do not match.',
    setupComplete: 'Account created. You can now sign in.',
    sessionExpired: 'Your session has expired. Please sign in again.',
  },
} as const;

export type TranslationKey = keyof typeof translations.fr;

export function translate(language: Language, key: TranslationKey): string {
  return translations[language][key];
}

export function browserLanguage(): Language {
  for (const language of navigator.languages) {
    const base = language.toLowerCase().split('-')[0];
    if (base === 'fr' || base === 'en') return base;
  }
  return 'fr';
}
