const sectionHeading = 'font-headline-md text-headline-md mb-3';
const body = 'font-body-lg text-body-lg text-on-surface-variant';
const list = `${body} list-disc pl-6 space-y-2 mt-3`;

export default function PrivacyPolicyPage() {
  return (
    <main className="max-w-3xl mx-auto px-6 py-12 text-on-surface">
      <h1 className="text-page-title text-on-surface mb-8">Privacy Policy</h1>

      <section className="mb-8">
        <h2 className={sectionHeading}>1. Information We Collect</h2>
        <p className={body}>We collect only what the service needs to work:</p>
        <ul className={list}>
          <li>
            <strong>Your account</strong> — your email address, your password (stored only as a
            salted hash), your display name, your theme choice, and the preview tier you pick in
            Settings.
          </li>
          <li>
            <strong>Your iRacing customer ID</strong>, if you link one. We record the ID you enter;
            we do not currently verify that it belongs to you.
          </li>
          <li>
            <strong>Telemetry you upload.</strong> When you upload an iRacing <code>.ibt</code> file
            we read it and keep, for each timed lap: the lap time, the car, the track, the session
            type, the air and track temperature, how wet the track was, when it was recorded, and
            the customer ID of the driver who recorded it. The file names that driver, and we check
            the name and ID against your linked account before saving anything — but we do not keep
            the driver&apos;s name, and we do not keep the file itself.
          </li>
          <li>
            <strong>What you do in the app</strong> — the drivers you follow as rivals, and the
            percentile and recommendation results we calculate for you.
          </li>
          <li>
            <strong>Sign-in security records</strong> — short-lived counts of failed sign-in
            attempts for your account and the network address they came from, and the browsers you
            have signed in from (see section 8). These exist so that someone guessing at your
            password cannot lock you out.
          </li>
        </ul>
      </section>

      <section className="mb-8">
        <h2 className={sectionHeading}>2. Information About Other Drivers</h2>
        <p className={body}>
          ApexRacers shows iRacing race results, standings, and leaderboards. These include other
          drivers&apos; iRacing display names, customer IDs, finishing positions, lap times, and
          ratings, whether or not those drivers have an ApexRacers account. This information comes
          from iRacing, where it is already published, and pages that show it can be viewed without
          signing in. When you look up a driver — for example to compare yourself with a rival — we
          fetch that driver&apos;s iRacing statistics and keep a copy for a short time (see section
          6).
        </p>
      </section>

      <section className="mb-8">
        <h2 className={sectionHeading}>3. How We Use Your Information</h2>
        <p className={body}>
          Your information is used solely to provide the ApexRacers service — signing you in and
          keeping your account secure, retrieving iRacing data, and generating car recommendations
          and percentile statistics. We use your email address to send account messages: confirming
          your address, resetting your password, confirming an email change, and warning you when
          someone appears to be guessing at your password. We do not sell your information, and we
          do not use it for advertising.
        </p>
      </section>

      <section className="mb-8">
        <h2 className={sectionHeading}>4. Service Providers</h2>
        <p className={body}>
          We rely on the following services, and they receive information only to do this job for
          us:
        </p>
        <ul className={list}>
          <li>
            <strong>Microsoft Azure</strong> hosts the application and its database in the United
            States (West US 3). Azure Application Insights stores our request logs (see section 5),
            and Azure Communication Services delivers our emails, which means it receives your email
            address and the message we send you.
          </li>
          <li>
            <strong>iRacing</strong> is the source of the racing data. We request driver statistics
            from iRacing by customer ID. Car and track pictures in the catalog are loaded by your
            browser directly from iRacing&apos;s image server, so viewing them sends your IP address
            and browser details to iRacing, as visiting any website would.
          </li>
        </ul>
        <p className={`${body} mt-3`}>
          The site loads no other outside resources: its fonts are served by ApexRacers itself, and
          there are no third-party analytics, advertising, or tracking scripts.
        </p>
      </section>

      <section className="mb-8">
        <h2 className={sectionHeading}>5. Request Logs</h2>
        <p className={body}>
          Every request to ApexRacers is logged with the page or API address requested, the result,
          how long it took, and the IP address it came from. We use these logs to keep the service
          running and to investigate abuse. They are kept for 90 days and then deleted
          automatically. The emails we send are logged by subject only, never by recipient.
        </p>
      </section>

      <section className="mb-8">
        <h2 className={sectionHeading}>6. Data Retention and Deletion</h2>
        <p className={body}>
          We keep your account data for as long as your account exists. To delete your account,
          email us at the address in section 9. There is no self-service option yet. Deleting your
          account removes it and everything attached to it: your profile, your linked customer ID,
          your uploaded laps, the rivals you follow, your percentile and recommendation results,
          your sign-in sessions, the browsers recognised for your account, and your sign-in security
          records.
        </p>
        <p className={`${body} mt-3`}>
          Some information is not attached to your account, so deleting it does not remove it:
        </p>
        <ul className={list}>
          <li>
            iRacing race results that include you, which are iRacing&apos;s published records (see
            section 2).
          </li>
          <li>
            Other members who follow your customer ID as a rival keep that entry, because it belongs
            to their account.
          </li>
          <li>
            Copies of iRacing statistics we looked up expire on their own, and are removed within a
            few days.
          </li>
          <li>Request logs are deleted after 90 days (see section 5).</li>
          <li>
            Database backups are kept for 7 days, so deleted information can remain in a backup
            until it ages out.
          </li>
        </ul>
      </section>

      <section className="mb-8">
        <h2 className={sectionHeading}>7. Security</h2>
        <p className={body}>
          Passwords are stored as salted hashes and are never stored in plain text. Sign-in session
          tokens are stored on our side only as hashes. All data transmission occurs over HTTPS.
        </p>
      </section>

      <section className="mb-8">
        <h2 className={sectionHeading}>8. Cookies and Browser Storage</h2>
        <p className={body}>
          We set one cookie, and only when you sign in successfully. It holds a random identifier
          that means nothing outside this service — no name, no email address, and nothing about you
          or your browsing — and it lets us recognise a browser you have signed in from before, so
          that someone repeatedly guessing at your password from the same network cannot stop you
          signing in. It does not sign you in by itself, and it is not used for advertising,
          analytics, or tracking you across other sites. It lasts 90 days from your most recent
          sign-in, and clearing your browser&apos;s cookies removes it — you will simply be treated
          as a new browser the next time you sign in. Changing or resetting your password, or
          changing your email address, makes us forget every browser recognised for your account.
        </p>
        <p className={`${body} mt-3`}>
          The app also stores a few things in your browser rather than in a cookie. While you are
          signed in, it keeps your sign-in tokens in the browser&apos;s local database: one that
          lasts 15 minutes and one that lasts 7 days and keeps you signed in. Signing out removes
          them. It also remembers your theme and whether you collapsed the sidebar. None of this is
          used for tracking.
        </p>
      </section>

      <section className="mb-8">
        <h2 className={sectionHeading}>9. Contact</h2>
        <p className={body}>
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
        Last updated: September 2026
      </p>
    </main>
  );
}
