using NUnit.Framework;
using Ryujinx.HLE.HOS;
using System;

namespace Ryujinx.Tests.HLE
{
    public class NextendoJustDancePatchTests
    {
        [Test]
        public void ExactBuild0RejectsConflictsAndKeepsExecutablePadding() {
            const string id="E63D7A75EE916F07C9AAC2985A9F5CFC3114456A000000000000000000000000";
            byte[] program=new byte[13492224];
            Convert.FromHexString("8a070054").CopyTo(program,11320936);
            Convert.FromHexString("299bff97").CopyTo(program,11320924);
            Convert.FromHexString("03070094").CopyTo(program,11671636);
            byte[] original=(byte[])program.Clone();
            Assert.That(NextendoJustDancePatch.Apply(0,id,"main",program),Is.False);
            Assert.That(NextendoJustDancePatch.Apply(NextendoJustDancePatch.TitleId,id,"sdk",program),Is.False);
            Assert.That(NextendoJustDancePatch.Apply(NextendoJustDancePatch.TitleId,"UNKNOWN","main",program),Is.False);
            Assert.That(program,Is.EqualTo(original));
            program[11320936]^=1;
            byte[] conflicted=(byte[])program.Clone();
            Assert.That(NextendoJustDancePatch.Apply(NextendoJustDancePatch.TitleId,id,"main",program),Is.False);
            Assert.That(program,Is.EqualTo(conflicted));
            program=original;
            Assert.That(NextendoJustDancePatch.Apply(NextendoJustDancePatch.TitleId,id,"main",program),Is.True);
            byte[] patched=(byte[])program.Clone();
            Assert.That(NextendoJustDancePatch.Apply(NextendoJustDancePatch.TitleId,id,"main",program),Is.True);
            Assert.That(program,Is.EqualTo(patched));
            Assert.That(NextendoJustDancePatch.PaddedTextSize(id,"main",0,13488688,13492224),Is.EqualTo(13492224u));
            Assert.That(NextendoJustDancePatch.PaddedTextSize(id,"sdk",0,13488688,13492224),Is.EqualTo(13488688u));
            Assert.That(NextendoJustDancePatch.PaddedTextSize("UNKNOWN","main",0,13488688,13492224),Is.EqualTo(13488688u));
        }
        [Test]
        public void ExactBuild1RejectsConflictsAndKeepsExecutablePadding() {
            const string id="ED8426574258F6728BE883C22010730D5D6DB531000000000000000000000000";
            byte[] program=new byte[13492224];
            Convert.FromHexString("8a070054").CopyTo(program,11321464);
            Convert.FromHexString("299bff97").CopyTo(program,11321452);
            Convert.FromHexString("03070094").CopyTo(program,11672164);
            byte[] original=(byte[])program.Clone();
            Assert.That(NextendoJustDancePatch.Apply(0,id,"main",program),Is.False);
            Assert.That(NextendoJustDancePatch.Apply(NextendoJustDancePatch.TitleId,id,"sdk",program),Is.False);
            Assert.That(NextendoJustDancePatch.Apply(NextendoJustDancePatch.TitleId,"UNKNOWN","main",program),Is.False);
            Assert.That(program,Is.EqualTo(original));
            program[11321464]^=1;
            byte[] conflicted=(byte[])program.Clone();
            Assert.That(NextendoJustDancePatch.Apply(NextendoJustDancePatch.TitleId,id,"main",program),Is.False);
            Assert.That(program,Is.EqualTo(conflicted));
            program=original;
            Assert.That(NextendoJustDancePatch.Apply(NextendoJustDancePatch.TitleId,id,"main",program),Is.True);
            byte[] patched=(byte[])program.Clone();
            Assert.That(NextendoJustDancePatch.Apply(NextendoJustDancePatch.TitleId,id,"main",program),Is.True);
            Assert.That(program,Is.EqualTo(patched));
            Assert.That(NextendoJustDancePatch.PaddedTextSize(id,"main",0,13489216,13492224),Is.EqualTo(13492224u));
            Assert.That(NextendoJustDancePatch.PaddedTextSize(id,"sdk",0,13489216,13492224),Is.EqualTo(13489216u));
            Assert.That(NextendoJustDancePatch.PaddedTextSize("UNKNOWN","main",0,13489216,13492224),Is.EqualTo(13489216u));
        }
    }
}
