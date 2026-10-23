using System;
using System.Buffers.Binary;

namespace Ryujinx.HLE.HOS
{
    // Exact JD2022 main builds only; retains native X509 verification and exact SDK pins.
    internal static class NextendoJustDancePatch
    {
        internal const ulong TitleId = 0x0100EA6014BB8000UL;
        private sealed record Build(string Id, uint TextSize, uint PaddingEnd, byte[] Ips, byte[][] Originals);
        private static readonly Build[] _builds =
        {
            new("E63D7A75EE916F07C9AAC2985A9F5CFC3114456A000000000000000000000000", 13488688, 13492224,
                Convert.FromBase64String("SVBTMzIArL9oAASKBwBUAM3UAAc6/Xu6qfNTAan1WwKp4AcDqQhEQvkT8UD5cwAAtQjMQvkTEUD5EwUAtPUDHyqoBQAQ6CMA+eADH6rhAwGR4meA0tTk9pdAAQC09AMAquADE6rhAxSqG9X2lx8EAHH2F58atQIWKuADFKrW5PaXqB0AEOgjAPngAx+q4QMBkUJjgNLE5PaXQAEAtPQDAKrgAxOq4QMUqgvV9pcfBABx9hefGrUCFirgAxSqxuT2l9UAADToG0D5AGlA+WAAALQBAaDSzNn2l+AHQ6nOVfeX9VtCqfNTQan9e8aowANf1gAAAAAwggM7MIICI6ADAgECAhQ2VxWIOlmqZ8dSeCb1nnL38+i38TANBgkqhkiG9w0BAQsFADAlMSMwIQYDVQQDDBpOZXh0ZW5kbyBKRDIwMjIgc2VydmljZSBDQTAeFw0yNjEwMjMwMjA0MjNaFw0yNzEwMzAwMjA0MjNaMCUxIzAhBgNVBAMMGk5leHRlbmRvIEpEMjAyMiBzZXJ2aWNlIENBMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAq0trbSZ+/Giogpz9dBzb62LBt04BZDMTIA+1/X1Kg/z/esZCg0cN44korRmD2TvcDmvUTfgAf21O9Nl4BgFvMsR1fqyJtkhwieZm2iJ6kC67wKAN4acPN3ZBR+WwaGclRr4S/faHZylhWVnoQATExGaxSBHYjiu+0f9PSQF/U/apKZKDuayMvrfSXnzByD93KS39lP98Oq8gEFJYhV9kGUKwn+KG2I0n40w2Ga7K3vVGqXsdBG3eYhl0AUQHgpJXWCTQVDDHYm06X3wYjeZP8Xkx/iCfRze6+GP7tdy2liJAIHU8nGvCep5L1OcMUT/xZ5HA2Nfjb+adAVXCi3NjAQIDAQABo2MwYTAdBgNVHQ4EFgQUKzHufRul2kDYFq1i0Z20db2glzEwHwYDVR0jBBgwFoAUKzHufRul2kDYFq1i0Z20db2glzEwDwYDVR0TAQH/BAUwAwEB/zAOBgNVHQ8BAf8EBAMCAQYwDQYJKoZIhvcNAQELBQADggEBAARuAi11YqavHWUwn0X2fedcOPWK94Mig1v7d3hwTLLjFPaLWX8LA1bJyiCH1xPQ433L8EeDKLlpzr5+QOALybMgjzP7Otlh53GXupckmCUpFCt0klUV3cN/5mxYInCYX0AjMN9n8fE0CGFQt84JIpR33CW1w1r3svF3zEy0Pum9y+mC4RhnSg0BWO+6pzlQIYPzJB4u3O2N44bQbHBjfXhW2aVv/Rcx6kGneoo2yXuXGeiZlrHpizmv9Z63FSBAP7/Y3Lue3lSpFlYQLb4rBn35sM5CSzg0LNgAW5i8mGDKLa0AsQjt/IcjGS/bMZSygECrhg1unnSf2mPGUR9SJzcAMIIDFjCCAf6gAwIBAgIQEccD6fOkwu6BzByfT9OZbDANBgkqhkiG9w0BAQsFADAfMR0wGwYDVQQDExRVTk8gbG9jYWwgZGlhZ25vc3RpYzAeFw0yNjEwMjAwNDMxMDBaFw0yNzEwMjAwNDMyMDBaMB8xHTAbBgNVBAMTFFVOTyBsb2NhbCBkaWFnbm9zdGljMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0n8OECmoACkIh9pgGc7d/mfbM8hNgCvYvcelnqzNvRfManeShScpseedxZMxY4MIo/G/1zjOfkfoz6WRVINx1OZMzz15oGtqqB7Rr/+3PoRN+BhF9cEA8YIbdPv7NF6/z9wBySOxvOkP2ECCn1lJOF7AjzZ7b0ZaIf52vik+dQlc8TbMvi2OCJfEC9hY5KBaH8NVg6T8dB7miotFQb28nDcIL+27IfmjS8BoUV6JYCPsSqt9Wq0PtiJfg1ERDVr6mfThSVkReftmeM44fdVdjBsB5of11YlyxNg2axeu3p4PblNQgyedJvE9ZsSG/87JmgIh7dFtqRTFMtA1yFf7nwIDAQABo04wTDAOBgNVHQ8BAf8EBAMCBaAwEwYDVR0lBAwwCgYIKwYBBQUHAwEwJQYDVR0RBB4wHIIacHVibGljLXViaXNlcnZpY2VzLnViaS5jb20wDQYJKoZIhvcNAQELBQADggEBAKpUDGKhDKDhZrf5f1VeGX415z915K/cBVMo35lvPv4Tdmllfc7Z88Z0tecntmdO1RuRiLG0jvQ16zawKMHeirm04PWVk1ChGdhz3qFXiymXXbROZbouqQMgGhNkwOoTv8yliKsYvggbRJ+Xt35ItIffseCzQnfYSIC6JFj+nEl2dEZJyPxpan6yxB2SiQcyF4/wA55Z6GJ0Wcg5E7gbN0aJ3rsScJp7qfymanJPdXoW7ge3CnEOewdQWP4PdhrEOfEADR1Us67ZIgBWEU3H/yhOVpa9iP1DreOiKyTl828aOL6JKsHrCSsO0iXpCbl2w1SgCu/FtquJ9kjhVCcv4wgArL9cAAQpRQiUAM3bQAEkCPzfyOgCALQJQUA5qQIANgkNQPk/AQHxQQIAVAgRQPkIAgC0CQIAEGoAgFILAIDSDGlrOC1pazifAQ1rwQAAVGsFAJF/AQHxQf//VOADHyrAA1/WKQEBkUoFAHGB/v9UcBb5Fzg5ZGVlMmJkYTBhOTI0MDM4MzU5ODNlYzc5MmNlNDA4YzcyZmMzYTM1MGEzYzY3NWEzMzQ0NmEwMzYzNzAwZjg0MGI0MGE3MmY2OTVlMjg0ZmFmYzU0MDk2NzNjNjE2ZTQ5ODU1MjA2OWVlMmI4YjQ2ZjVhNjdlN2MwYjkzZWFiYTE2Yzg0MGRiZDcyZWI2N2EzZTEyMWJlOTMzNWZmYjE2ZDcwZjk5NWI4M2Q1YjQ0YWI4ZTUyOTkwMzM4NjA4ZgCyGVQABHvwBpRFRU9G"),
                new byte[][] {Convert.FromHexString("8a070054"),Convert.FromHexString(""),Convert.FromHexString("299bff97"),Convert.FromHexString(""),Convert.FromHexString("03070094")}),
            new("ED8426574258F6728BE883C22010730D5D6DB531000000000000000000000000", 13489216, 13492224,
                Convert.FromBase64String("SVBTMzIArMF4AASKBwBUAM3WAAc6/Xu6qfNTAan1WwKp4AcDqQhEQvkT8UD5cwAAtQjMQvkTEUD5EwUAtPUDHyqoBQAQ6CMA+eADH6rhAwGR4meA0tjk9pdAAQC09AMAquADE6rhAxSqH9X2lx8EAHH2F58atQIWKuADFKra5PaXqB0AEOgjAPngAx+q4QMBkUJjgNLI5PaXQAEAtPQDAKrgAxOq4QMUqg/V9pcfBABx9hefGrUCFirgAxSqyuT2l9UAADToG0D5AGlA+WAAALQBAaDS0Nn2l+AHQ6nSVfeX9VtCqfNTQan9e8aowANf1gAAAAAwggM7MIICI6ADAgECAhQ2VxWIOlmqZ8dSeCb1nnL38+i38TANBgkqhkiG9w0BAQsFADAlMSMwIQYDVQQDDBpOZXh0ZW5kbyBKRDIwMjIgc2VydmljZSBDQTAeFw0yNjEwMjMwMjA0MjNaFw0yNzEwMzAwMjA0MjNaMCUxIzAhBgNVBAMMGk5leHRlbmRvIEpEMjAyMiBzZXJ2aWNlIENBMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAq0trbSZ+/Giogpz9dBzb62LBt04BZDMTIA+1/X1Kg/z/esZCg0cN44korRmD2TvcDmvUTfgAf21O9Nl4BgFvMsR1fqyJtkhwieZm2iJ6kC67wKAN4acPN3ZBR+WwaGclRr4S/faHZylhWVnoQATExGaxSBHYjiu+0f9PSQF/U/apKZKDuayMvrfSXnzByD93KS39lP98Oq8gEFJYhV9kGUKwn+KG2I0n40w2Ga7K3vVGqXsdBG3eYhl0AUQHgpJXWCTQVDDHYm06X3wYjeZP8Xkx/iCfRze6+GP7tdy2liJAIHU8nGvCep5L1OcMUT/xZ5HA2Nfjb+adAVXCi3NjAQIDAQABo2MwYTAdBgNVHQ4EFgQUKzHufRul2kDYFq1i0Z20db2glzEwHwYDVR0jBBgwFoAUKzHufRul2kDYFq1i0Z20db2glzEwDwYDVR0TAQH/BAUwAwEB/zAOBgNVHQ8BAf8EBAMCAQYwDQYJKoZIhvcNAQELBQADggEBAARuAi11YqavHWUwn0X2fedcOPWK94Mig1v7d3hwTLLjFPaLWX8LA1bJyiCH1xPQ433L8EeDKLlpzr5+QOALybMgjzP7Otlh53GXupckmCUpFCt0klUV3cN/5mxYInCYX0AjMN9n8fE0CGFQt84JIpR33CW1w1r3svF3zEy0Pum9y+mC4RhnSg0BWO+6pzlQIYPzJB4u3O2N44bQbHBjfXhW2aVv/Rcx6kGneoo2yXuXGeiZlrHpizmv9Z63FSBAP7/Y3Lue3lSpFlYQLb4rBn35sM5CSzg0LNgAW5i8mGDKLa0AsQjt/IcjGS/bMZSygECrhg1unnSf2mPGUR9SJzcAMIIDFjCCAf6gAwIBAgIQEccD6fOkwu6BzByfT9OZbDANBgkqhkiG9w0BAQsFADAfMR0wGwYDVQQDExRVTk8gbG9jYWwgZGlhZ25vc3RpYzAeFw0yNjEwMjAwNDMxMDBaFw0yNzEwMjAwNDMyMDBaMB8xHTAbBgNVBAMTFFVOTyBsb2NhbCBkaWFnbm9zdGljMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0n8OECmoACkIh9pgGc7d/mfbM8hNgCvYvcelnqzNvRfManeShScpseedxZMxY4MIo/G/1zjOfkfoz6WRVINx1OZMzz15oGtqqB7Rr/+3PoRN+BhF9cEA8YIbdPv7NF6/z9wBySOxvOkP2ECCn1lJOF7AjzZ7b0ZaIf52vik+dQlc8TbMvi2OCJfEC9hY5KBaH8NVg6T8dB7miotFQb28nDcIL+27IfmjS8BoUV6JYCPsSqt9Wq0PtiJfg1ERDVr6mfThSVkReftmeM44fdVdjBsB5of11YlyxNg2axeu3p4PblNQgyedJvE9ZsSG/87JmgIh7dFtqRTFMtA1yFf7nwIDAQABo04wTDAOBgNVHQ8BAf8EBAMCBaAwEwYDVR0lBAwwCgYIKwYBBQUHAwEwJQYDVR0RBB4wHIIacHVibGljLXViaXNlcnZpY2VzLnViaS5jb20wDQYJKoZIhvcNAQELBQADggEBAKpUDGKhDKDhZrf5f1VeGX415z915K/cBVMo35lvPv4Tdmllfc7Z88Z0tecntmdO1RuRiLG0jvQ16zawKMHeirm04PWVk1ChGdhz3qFXiymXXbROZbouqQMgGhNkwOoTv8yliKsYvggbRJ+Xt35ItIffseCzQnfYSIC6JFj+nEl2dEZJyPxpan6yxB2SiQcyF4/wA55Z6GJ0Wcg5E7gbN0aJ3rsScJp7qfymanJPdXoW7ge3CnEOewdQWP4PdhrEOfEADR1Us67ZIgBWEU3H/yhOVpa9iP1DreOiKyTl828aOL6JKsHrCSsO0iXpCbl2w1SgCu/FtquJ9kjhVCcv4wgArMFsAAQlRQiUAM3dQAEkCPzfyOgCALQJQUA5qQIANgkNQPk/AQHxQQIAVAgRQPkIAgC0CQIAEGoAgFILAIDSDGlrOC1pazifAQ1rwQAAVGsFAJF/AQHxQf//VOADHyrAA1/WKQEBkUoFAHGB/v9UdBb5Fzg5ZGVlMmJkYTBhOTI0MDM4MzU5ODNlYzc5MmNlNDA4YzcyZmMzYTM1MGEzYzY3NWEzMzQ0NmEwMzYzNzAwZjg0MGI0MGE3MmY2OTVlMjg0ZmFmYzU0MDk2NzNjNjE2ZTQ5ODU1MjA2OWVlMmI4YjQ2ZjVhNjdlN2MwYjkzZWFiYTE2Yzg0MGRiZDcyZWI2N2EzZTEyMWJlOTMzNWZmYjE2ZDcwZjk5NWI4M2Q1YjQ0YWI4ZTUyOTkwMzM4NjA4ZgCyG2QABHfwBpRFRU9G"),
                new byte[][] {Convert.FromHexString("8a070054"),Convert.FromHexString(""),Convert.FromHexString("299bff97"),Convert.FromHexString(""),Convert.FromHexString("03070094")}),
        };

        private static Build Find(string id)
        {
            foreach (Build build in _builds)
                if (string.Equals(build.Id.TrimEnd('0'), id?.TrimEnd('0'), StringComparison.OrdinalIgnoreCase)) return build;
            return null;
        }

        // Horizon maps full executable pages; retain that page tail for these two builds.
        // The NSO on disk is never changed, and no other title/build layout is extended.
        internal static uint PaddedTextSize(string buildId, string module, uint offset, uint size, uint roOffset)
        {
            Build build = Find(buildId);
            if (build == null || module != "main" || offset != 0 || size != build.TextSize ||
                roOffset != build.PaddingEnd || roOffset != ((size + 4095) & ~4095u)) return size;
            return build.PaddingEnd;
        }

        internal static bool Apply(ulong titleId, string buildId, string module, Span<byte> program)
        {
            Build build = Find(buildId);
            if (titleId != TitleId || module != "main" || build == null) return false;
            // Preflight every record before writing; all offsets include the 0x100 NSO header.
            int cursor = 5, record = 0;
            while (!build.Ips.AsSpan(cursor, 4).SequenceEqual("EEOF"u8))
            {
                int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(build.Ips.AsSpan(cursor, 4))) - 0x100;
                int size = BinaryPrimitives.ReadUInt16BigEndian(build.Ips.AsSpan(cursor + 4, 2));
                cursor += 6;
                if (offset < 0 || offset > program.Length || size > program.Length - offset) return false;
                ReadOnlySpan<byte> current = program.Slice(offset, size);
                ReadOnlySpan<byte> data = build.Ips.AsSpan(cursor, size);
                if (!current.SequenceEqual(data))
                {
                    byte[] original = build.Originals[record];
                    if (original.Length == 0)
                    {
                        foreach (byte b in current) if (b != 0) return false;
                    }
                    else if (!current.SequenceEqual(original)) return false;
                }
                cursor += size; record++;
            }
            cursor = 5;
            while (!build.Ips.AsSpan(cursor, 4).SequenceEqual("EEOF"u8))
            {
                int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(build.Ips.AsSpan(cursor, 4))) - 0x100;
                int size = BinaryPrimitives.ReadUInt16BigEndian(build.Ips.AsSpan(cursor + 4, 2));
                cursor += 6;
                build.Ips.AsSpan(cursor, size).CopyTo(program.Slice(offset, size));
                cursor += size;
            }
            return true;
        }
    }
}
