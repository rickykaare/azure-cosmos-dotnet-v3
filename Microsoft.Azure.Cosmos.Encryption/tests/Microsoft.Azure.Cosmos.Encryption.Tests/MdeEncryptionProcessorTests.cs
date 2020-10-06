﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;
    using Newtonsoft.Json.Linq;
    using TestDoc = TestCommon.TestDoc;

    [TestClass]
    public class MdeEncryptionProcessorTests
    {
        private static Mock<Encryptor> mockEncryptor;
        private static EncryptionOptions encryptionOptions;
        private const string dekId = "dekId";
        private static MdeEncryptionProcessor mdeEncryptionProcessor;

        [ClassInitialize]
        public static void ClassInitilize(TestContext testContext)
        {
            _ = testContext;
            mdeEncryptionProcessor = new MdeEncryptionProcessor();
            MdeEncryptionProcessorTests.encryptionOptions = new EncryptionOptions()
            {
                DataEncryptionKeyId = MdeEncryptionProcessorTests.dekId,
                EncryptionAlgorithm = CosmosEncryptionAlgorithm.MdeAEAes256CbcHmacSha256Randomized,
                PathsToEncrypt = TestDoc.PathsToEncrypt
            };

            MdeEncryptionProcessorTests.mockEncryptor = new Mock<Encryptor>();
            MdeEncryptionProcessorTests.mockEncryptor.Setup(m => m.EncryptAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[] plainText, string dekId, string algo, CancellationToken t) =>
                    dekId == MdeEncryptionProcessorTests.dekId ? TestCommon.EncryptData(plainText) : throw new InvalidOperationException("DEK not found."));
            MdeEncryptionProcessorTests.mockEncryptor.Setup(m => m.DecryptAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[] cipherText, string dekId, string algo, CancellationToken t) => 
                    dekId == MdeEncryptionProcessorTests.dekId ? TestCommon.DecryptData(cipherText) : throw new InvalidOperationException("Null DEK was returned."));
        }

        [TestMethod]
        public async Task InvalidPathToEncrypt()
        {
            TestDoc testDoc = TestDoc.Create();
            EncryptionOptions encryptionOptionsWithInvalidPathToEncrypt = new EncryptionOptions()
            {
                DataEncryptionKeyId = MdeEncryptionProcessorTests.dekId,
                EncryptionAlgorithm = CosmosEncryptionAlgorithm.MdeAEAes256CbcHmacSha256Randomized,
                PathsToEncrypt = new List<string>() { "/SensitiveStr", "/Invalid" }
            };

            try
            {
                await MdeEncryptionProcessorTests.mdeEncryptionProcessor.EncryptAsync(
                    testDoc.ToStream(),
                    MdeEncryptionProcessorTests.mockEncryptor.Object,
                    encryptionOptionsWithInvalidPathToEncrypt,
                    new CosmosDiagnosticsContext(),
                    CancellationToken.None);

                Assert.Fail("Invalid path to encrypt didn't result in exception.");
            }
            catch (ArgumentException ex)
            {
                Assert.AreEqual("PathsToEncrypt includes a path: '/Invalid' which was not found.", ex.Message);
            }
        }

        [TestMethod]
        public async Task EncryptDecryptPropertyWithNullValue()
        {        
            TestDoc testDoc = TestDoc.Create();
            testDoc.SensitiveStr = null;

            JObject encryptedDoc = await MdeEncryptionProcessorTests.VerifyEncryptionSucceeded(testDoc);

            JObject decryptedDoc = await MdeEncryptionProcessorTests.mdeEncryptionProcessor.DecryptAsync(
               encryptedDoc,
               MdeEncryptionProcessorTests.mockEncryptor.Object,
               new CosmosDiagnosticsContext(),
               CancellationToken.None);

            MdeEncryptionProcessorTests.VerifyDecryptionSucceeded(
                decryptedDoc,
                testDoc);
        }

        [TestMethod]
        public async Task ValidateEncryptDecryptDocument()
        {
            TestDoc testDoc = TestDoc.Create();

            JObject encryptedDoc = await MdeEncryptionProcessorTests.VerifyEncryptionSucceeded(testDoc);

            JObject decryptedDoc = await MdeEncryptionProcessorTests.mdeEncryptionProcessor.DecryptAsync(
                encryptedDoc,
                MdeEncryptionProcessorTests.mockEncryptor.Object,
                new CosmosDiagnosticsContext(),
                CancellationToken.None);

            MdeEncryptionProcessorTests.VerifyDecryptionSucceeded(
                decryptedDoc,
                testDoc);
        }

        [TestMethod]
        public async Task ValidateDecryptStream()
        {
            TestDoc testDoc = TestDoc.Create();

            Stream encryptedStream = await MdeEncryptionProcessorTests.mdeEncryptionProcessor.EncryptAsync(
                testDoc.ToStream(),
                MdeEncryptionProcessorTests.mockEncryptor.Object,
                MdeEncryptionProcessorTests.encryptionOptions,
                new CosmosDiagnosticsContext(),
                CancellationToken.None);

            Stream decryptedStream = await MdeEncryptionProcessorTests.mdeEncryptionProcessor.DecryptAsync(
                encryptedStream,
                MdeEncryptionProcessorTests.mockEncryptor.Object,
                new CosmosDiagnosticsContext(),
                CancellationToken.None);

            JObject decryptedDoc = MdeEncryptionProcessor.BaseSerializer.FromStream<JObject>(decryptedStream);
            MdeEncryptionProcessorTests.VerifyDecryptionSucceeded(
                decryptedDoc,
                testDoc);
        }

        [TestMethod]
        public async Task DecryptStreamWithoutEncryptedProperty()
        {
            TestDoc testDoc = TestDoc.Create();
            Stream docStream = testDoc.ToStream();

            Stream decryptedStream = await MdeEncryptionProcessorTests.mdeEncryptionProcessor.DecryptAsync(
                docStream,
                MdeEncryptionProcessorTests.mockEncryptor.Object,
                new CosmosDiagnosticsContext(),
                CancellationToken.None);

            Assert.IsTrue(decryptedStream.CanSeek);
            Assert.AreEqual(0, decryptedStream.Position);
            Assert.AreEqual(docStream.Length, decryptedStream.Length);
        }

        private static async Task<JObject> VerifyEncryptionSucceeded(TestDoc testDoc)
        {
            Stream encryptedStream = await MdeEncryptionProcessorTests.mdeEncryptionProcessor.EncryptAsync(
                 testDoc.ToStream(),
                 MdeEncryptionProcessorTests.mockEncryptor.Object,
                 MdeEncryptionProcessorTests.encryptionOptions,
                 new CosmosDiagnosticsContext(),
                 CancellationToken.None);

            JObject encryptedDoc = EncryptionProcessor.BaseSerializer.FromStream<JObject>(encryptedStream);

            Assert.AreEqual(testDoc.Id, encryptedDoc.Property("id").Value.Value<string>());
            Assert.AreEqual(testDoc.PK, encryptedDoc.Property(nameof(TestDoc.PK)).Value.Value<string>());
            Assert.AreEqual(testDoc.NonSensitive, encryptedDoc.Property(nameof(TestDoc.NonSensitive)).Value.Value<string>());
            Assert.IsNotNull(encryptedDoc.Property(nameof(TestDoc.SensitiveInt)).Value.Value<string>());
            Assert.AreNotEqual(testDoc.SensitiveInt, encryptedDoc.Property(nameof(TestDoc.SensitiveInt)).Value.Value<string>()); // not equal since value is encrypted

            JProperty eiJProp = encryptedDoc.Property(Constants.EncryptedInfo);
            Assert.IsNotNull(eiJProp);
            Assert.IsNotNull(eiJProp.Value);
            Assert.AreEqual(JTokenType.Object, eiJProp.Value.Type);
            EncryptionProperties encryptionProperties = ((JObject)eiJProp.Value).ToObject<EncryptionProperties>();

            Assert.IsNotNull(encryptionProperties);
            Assert.AreEqual(MdeEncryptionProcessorTests.dekId, encryptionProperties.DataEncryptionKeyId);
            Assert.AreEqual(3, encryptionProperties.EncryptionFormatVersion);
            Assert.IsNull(encryptionProperties.EncryptedData);
            Assert.IsNotNull(encryptionProperties.EncryptedPaths);

            if (testDoc.SensitiveStr == null)
            {
                Assert.IsNull(encryptedDoc.Property(nameof(TestDoc.SensitiveStr)).Value.Value<string>()); // since null value is not encrypted
                Assert.AreEqual(TestDoc.PathsToEncrypt.Count - 1, encryptionProperties.EncryptedPaths.Count());
            }
            else
            {
                Assert.IsNotNull(encryptedDoc.Property(nameof(TestDoc.SensitiveStr)).Value.Value<string>());
                Assert.AreNotEqual(testDoc.SensitiveStr, encryptedDoc.Property(nameof(TestDoc.SensitiveStr)).Value.Value<string>()); // not equal since value is encrypted
                Assert.AreEqual(TestDoc.PathsToEncrypt.Count, encryptionProperties.EncryptedPaths.Count());
            }

            return encryptedDoc;
        }

        private static void VerifyDecryptionSucceeded(
            JObject decryptedDoc,
            TestDoc expectedDoc)
        {
            Assert.AreEqual(expectedDoc.SensitiveStr, decryptedDoc.Property(nameof(TestDoc.SensitiveStr)).Value.Value<string>());
            Assert.AreEqual(expectedDoc.SensitiveInt, decryptedDoc.Property(nameof(TestDoc.SensitiveInt)).Value.Value<int>());
            Assert.IsNull(decryptedDoc.Property(Constants.EncryptedInfo));
        }
    }
}