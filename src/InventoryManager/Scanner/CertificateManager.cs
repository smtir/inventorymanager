using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InventoryManager.Data;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace InventoryManager.Scanner
{
    public sealed class ServerCredentials
    {
        public X509Certificate Certificate { get; set; }
        public AsymmetricKeyParameter PrivateKey { get; set; }
    }

    /// <summary>
    /// Creates and stores the self-signed certificate used by the phone scanner's HTTPS server.
    /// Browsers only allow camera access on HTTPS pages, so plain HTTP is not an option.
    /// </summary>
    public static class CertificateManager
    {
        private const string FileName = "scanner-cert.pem";

        public static ServerCredentials LoadOrCreate(IList<string> ipAddresses)
        {
            var path = Path.Combine(Database.DataFolder, FileName);
            var existing = TryLoad(path);
            if (existing != null && IsUsable(existing.Certificate, ipAddresses))
                return existing;

            var created = Create(ipAddresses);
            Save(path, created);
            return created;
        }

        private static bool IsUsable(X509Certificate cert, IList<string> ipAddresses)
        {
            if (cert.NotAfter < DateTime.UtcNow.AddDays(7))
                return false;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sans = cert.GetSubjectAlternativeNames();
            if (sans != null)
            {
                foreach (IList entry in sans)
                {
                    if (entry.Count >= 2 && entry[1] != null)
                        names.Add(entry[1].ToString());
                }
            }
            return ipAddresses.All(names.Contains);
        }

        private static ServerCredentials Create(IList<string> ipAddresses)
        {
            var random = new SecureRandom();
            var keyGen = new RsaKeyPairGenerator();
            keyGen.Init(new KeyGenerationParameters(random, 2048));
            AsymmetricCipherKeyPair keys = keyGen.GenerateKeyPair();

            var name = new X509Name("CN=Inventory Manager Scanner, O=Inventory Manager");
            var gen = new X509V3CertificateGenerator();
            gen.SetSerialNumber(BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random));
            gen.SetIssuerDN(name);
            gen.SetSubjectDN(name);
            gen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            gen.SetNotAfter(DateTime.UtcNow.AddDays(825));
            gen.SetPublicKey(keys.Public);

            var altNames = new List<GeneralName> { new GeneralName(GeneralName.DnsName, "localhost") };
            altNames.Add(new GeneralName(GeneralName.IPAddress, "127.0.0.1"));
            foreach (var ip in ipAddresses.Distinct())
                altNames.Add(new GeneralName(GeneralName.IPAddress, ip));

            gen.AddExtension(X509Extensions.SubjectAlternativeName, false, new GeneralNames(altNames.ToArray()));
            gen.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
            gen.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature | KeyUsage.KeyEncipherment));
            gen.AddExtension(X509Extensions.ExtendedKeyUsage, false, new ExtendedKeyUsage(new[] { KeyPurposeID.IdKPServerAuth }));

            var cert = gen.Generate(new Asn1SignatureFactory("SHA256WITHRSA", keys.Private, random));
            return new ServerCredentials { Certificate = cert, PrivateKey = keys.Private };
        }

        private static void Save(string path, ServerCredentials creds)
        {
            using (var writer = new StreamWriter(path, false))
            {
                var pem = new PemWriter(writer);
                pem.WriteObject(creds.Certificate);
                pem.WriteObject(creds.PrivateKey);
            }
        }

        private static ServerCredentials TryLoad(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                X509Certificate cert = null;
                AsymmetricKeyParameter key = null;
                using (var reader = new StreamReader(path))
                {
                    var pem = new PemReader(reader);
                    object o;
                    while ((o = pem.ReadObject()) != null)
                    {
                        if (o is X509Certificate) cert = (X509Certificate)o;
                        else if (o is AsymmetricCipherKeyPair) key = ((AsymmetricCipherKeyPair)o).Private;
                        else if (o is AsymmetricKeyParameter && ((AsymmetricKeyParameter)o).IsPrivate) key = (AsymmetricKeyParameter)o;
                    }
                }
                return cert != null && key != null ? new ServerCredentials { Certificate = cert, PrivateKey = key } : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
