using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class JsonTests
    {
        [Test]
        public void Json_ShouldRoundTrip()
        {
            var obj = new MasterDataJsonObject
            {
                { "s", "a\"b\\c\n\u0001日本" },
                { "i", 42 },
                { "l", long.MinValue },
                { "u", ulong.MaxValue },
                { "f", 0.1f },
                { "d", 1e300 },
                { "b", true },
                { "n", null },
                { "a", new List<object> { 1, "x" } },
                { "o", new MasterDataJsonObject() },
            };

            var parsed = (MasterDataJsonObject)MasterDataJson.Parse(MasterDataJson.Serialize(obj));

            Assert.AreEqual("a\"b\\c\n\u0001日本", parsed["s"]);
            Assert.AreEqual("42", ((MasterDataJsonNumber)parsed["i"]).Raw);
            Assert.AreEqual(long.MinValue.ToString(), ((MasterDataJsonNumber)parsed["l"]).Raw);
            Assert.AreEqual(ulong.MaxValue.ToString(), ((MasterDataJsonNumber)parsed["u"]).Raw);
            Assert.AreEqual(true, parsed["b"]);
            Assert.IsNull(parsed["n"]);
            Assert.AreEqual(2, ((List<object>)parsed["a"]).Count);
            Assert.AreEqual(obj.ToCanonicalString(), parsed.ToCanonicalString());
        }

        [TestCase("{")]
        [TestCase("{\"a\":}")]
        [TestCase("[1,]")]
        [TestCase("\"abc")]
        [TestCase("{} x")]
        [TestCase("tru")]
        public void Json_Invalid_ShouldThrow(string json)
        {
            Assert.Throws<System.FormatException>(() => MasterDataJson.Parse(json));
        }

        [Test]
        public void Values_ShouldConvertLosslessly()
        {
            AssertRoundTrip(ulong.MaxValue);
            AssertRoundTrip(long.MinValue);
            AssertRoundTrip(0.1f);
            AssertRoundTrip(float.MaxValue);
            AssertRoundTrip(double.Epsilon);
            AssertRoundTrip(float.NaN);
            AssertRoundTrip((short)-5);
            AssertRoundTrip((byte)255);
            AssertRoundTrip(TestElement.Ice);
            AssertRoundTrip(TestFlags.Boss | TestFlags.Flying);
            AssertRoundTrip(new Vector2(1.5f, -2f));
            AssertRoundTrip(new Vector3(1f, 2f, 3f));
            AssertRoundTrip(new Vector2Int(3, 4));
            AssertRoundTrip(new Color(0.1f, 0.2f, 0.3f, 0.4f));
            AssertRoundTrip("text");
        }

        [Test]
        public void Values_Invalid_ShouldThrow()
        {
            Assert.Throws<System.OverflowException>(() => MasterDataValueUtility.FromJson(new MasterDataJsonNumber("300"), typeof(byte)));
            Assert.Throws<System.FormatException>(() => MasterDataValueUtility.FromJson(new MasterDataJsonNumber("1.5"), typeof(int)));
            Assert.Throws<System.FormatException>(() => MasterDataValueUtility.FromJson(null, typeof(int)));
            Assert.IsNull(MasterDataValueUtility.FromJson(null, typeof(int?)));
            Assert.AreEqual(3, MasterDataValueUtility.FromJson(new MasterDataJsonNumber("3.0"), typeof(int)));
        }

        [Test]
        public void CustomValues_ShouldBeWrittenAsTextAndReadFromStringsOrNumbers()
        {
            using (MasterDataValueConverters.Register(new TestFixedConverter()))
            {
                Assert.AreEqual("-12.345", MasterDataValueUtility.ToJson(TestFixed.FromRaw(-12345)));

                // 2^53 + 1 thousandths: a double can not hold every digit
                const long raw = 9007199254740993;
                var number = ((List<object>)MasterDataJson.Parse("[9007199254740.993]"))[0];
                Assert.AreEqual(raw, ((TestFixed)MasterDataValueUtility.FromJson(number, typeof(TestFixed))).Raw, "a JSON number");
                Assert.AreEqual(raw, ((TestFixed)MasterDataValueUtility.FromJson("9007199254740.993", typeof(TestFixed))).Raw, "a JSON string");
                Assert.AreEqual(2500, ((TestFixed)MasterDataValueUtility.FromJson("2.5", typeof(TestFixed?))).Raw);
                Assert.IsNull(MasterDataValueUtility.FromJson(null, typeof(TestFixed?)));

                Assert.Throws<System.FormatException>(() => MasterDataValueUtility.FromJson("fast", typeof(TestFixed)));
                Assert.Throws<System.FormatException>(() => MasterDataValueUtility.FromJson(true, typeof(TestFixed)));
                Assert.Throws<System.FormatException>(() => MasterDataValueUtility.FromJson(null, typeof(TestFixed)));
            }
            Assert.Throws<System.NotSupportedException>(() => MasterDataValueUtility.ToJson(TestFixed.FromRaw(1)), "without the converter");
        }

        static void AssertRoundTrip<T>(T value)
        {
            var json = MasterDataJson.Serialize(new List<object> { MasterDataValueUtility.ToJson(value) });
            var parsed = ((List<object>)MasterDataJson.Parse(json))[0];
            Assert.AreEqual(value, MasterDataValueUtility.FromJson(parsed, typeof(T)), json);
        }
    }
}
