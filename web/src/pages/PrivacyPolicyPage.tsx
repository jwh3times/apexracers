export default function PrivacyPolicyPage() {
  return (
    <main className="max-w-3xl mx-auto px-6 py-12 text-on-surface">
      <h1 className="text-page-title text-on-surface mb-8">Privacy Policy</h1>

      <section className="mb-8">
        <h2 className="font-headline-md text-headline-md mb-3">1. Information We Collect</h2>
        <p className="font-body-lg text-body-lg text-on-surface-variant">
          We collect the information you provide when creating an account (email address and
          password) and your iRacing customer ID if you choose to link your account. We also collect
          lap time and session data retrieved from the iRacing API on your behalf.
        </p>
      </section>

      <section className="mb-8">
        <h2 className="font-headline-md text-headline-md mb-3">2. How We Use Your Information</h2>
        <p className="font-body-lg text-body-lg text-on-surface-variant">
          Your information is used solely to provide the ApexRacers service — authenticating your
          account, retrieving your iRacing data, and generating car recommendations and percentile
          statistics.
        </p>
      </section>

      <section className="mb-8">
        <h2 className="font-headline-md text-headline-md mb-3">3. Data Sharing</h2>
        <p className="font-body-lg text-body-lg text-on-surface-variant">
          We do not sell, trade, or otherwise transfer your personal information to third parties.
          Data is shared with iRacing.com only as necessary to retrieve your session statistics via
          their API.
        </p>
      </section>

      <section className="mb-8">
        <h2 className="font-headline-md text-headline-md mb-3">4. Data Retention</h2>
        <p className="font-body-lg text-body-lg text-on-surface-variant">
          Account data is retained for as long as your account is active. You may request deletion
          of your account and associated data at any time by contacting us.
        </p>
      </section>

      <section className="mb-8">
        <h2 className="font-headline-md text-headline-md mb-3">5. Security</h2>
        <p className="font-body-lg text-body-lg text-on-surface-variant">
          Passwords are stored as salted hashes and are never stored in plain text. All data
          transmission occurs over HTTPS.
        </p>
      </section>

      <section className="mb-8">
        <h2 className="font-headline-md text-headline-md mb-3">6. Contact</h2>
        <p className="font-body-lg text-body-lg text-on-surface-variant">
          For any privacy-related questions or data deletion requests, please contact us at{' '}
          <a
            href="mailto:privacy@apexracers.gg"
            className="text-primary underline hover:text-primary-fixed-dim transition-colors"
          >
            privacy@apexracers.gg
          </a>
          .
        </p>
      </section>

      <p className="font-body-sm text-body-sm text-on-surface-variant mt-12">
        Last updated: May 2026
      </p>
    </main>
  );
}
