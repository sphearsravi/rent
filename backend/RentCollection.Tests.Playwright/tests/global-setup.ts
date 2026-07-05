import { execSync } from 'child_process';
import crypto from 'crypto';

export default async function globalSetup() {
  console.log('Global setup: Seeding admin user in database...');

  // Generate PBKDF2 hash matching app's PasswordHasher format (salt:hash)
  const password = 'Admin@123';
  const salt = crypto.randomBytes(16);
  const hash = crypto.pbkdf2Sync(password, salt, 10000, 32, 'sha256');
  const passwordHash = `${salt.toString('base64')}:${hash.toString('base64')}`;

  const sql = `IF NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'admin') BEGIN INSERT INTO Users (Username, Email, PasswordHash, FirstName, LastName, Role, IsActive, EmailVerified, CreatedDate) VALUES ('admin', 'admin@test.com', '${passwordHash}', 'Admin', 'User', 'Admin', 1, 1, GETUTCDATE()) END ELSE BEGIN UPDATE Users SET PasswordHash = '${passwordHash}', Email = 'admin@test.com', IsActive = 1 WHERE Username = 'admin' END`;

  try {
    execSync(`sqlcmd -S "RAVIWST\\SQLEXPRESS" -d RentCollectionDB -Q "${sql}" -E`, {
      stdio: 'pipe',
      timeout: 10000,
    });
    console.log('Global setup: Admin user seeded successfully.');
  } catch (error) {
    console.error('Global setup: Failed to seed admin user:', error);
  }
}
