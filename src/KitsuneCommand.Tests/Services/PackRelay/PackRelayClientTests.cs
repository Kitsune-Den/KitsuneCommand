// PackRelayClient unit tests. Use a MockHttpMessageHandler that
// records every outgoing request and lets each test assert against
// the URL, headers, body, plus return canned responses (status +
// JSON body or raw bytes).
//
// Why hand-rolled vs MockHttp / RichardSzalay.MockHttp: KC's test
// project already uses Moq + NUnit; pulling in another HTTP mocking
// lib for what amounts to ~30 lines of TestMessageHandler is overkill.
// Hand-rolled also lets us assert against request streams cleanly,
// which matters for the multipart-chunk tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KitsuneCommand.Services.PackRelay;
using NUnit.Framework;

namespace KitsuneCommand.Tests.Services.PackRelay
{
    [TestFixture]
    public class PackRelayClientTests
    {
        private const string TestSha =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string ApiBase = "https://packrelay.cloud";
        private const string ApiToken = "test-token-abc";

        // ---- Construction ----

        [Test]
        public void Constructor_RejectsMissingBaseUrl()
        {
            Assert.Throws<ArgumentException>(() => new PackRelayClient(null, ApiToken));
            Assert.Throws<ArgumentException>(() => new PackRelayClient("", ApiToken));
        }

        [Test]
        public void Constructor_RejectsMissingToken()
        {
            Assert.Throws<ArgumentException>(() => new PackRelayClient(ApiBase, null));
            Assert.Throws<ArgumentException>(() => new PackRelayClient(ApiBase, ""));
        }

        [Test]
        public void Constructor_SetsBearerHeader()
        {
            var handler = new RecordingHandler();
            handler.QueueJson(HttpStatusCode.OK, "{\"exists\":false,\"size\":null}");
            using (var client = MakeClient(handler))
            {
                client.BlobExistsAsync(TestSha).GetAwaiter().GetResult();
            }
            var req = handler.Requests.Single();
            Assert.That(req.Headers.Authorization, Is.Not.Null);
            Assert.That(req.Headers.Authorization.Scheme, Is.EqualTo("Bearer"));
            Assert.That(req.Headers.Authorization.Parameter, Is.EqualTo(ApiToken));
        }

        // ---- BlobExists ----

        [Test]
        public async Task BlobExistsAsync_True_WhenCloudSaysExists()
        {
            var handler = new RecordingHandler();
            handler.QueueJson(HttpStatusCode.OK, "{\"exists\":true,\"size\":12345}");
            using (var client = MakeClient(handler))
            {
                var exists = await client.BlobExistsAsync(TestSha);
                Assert.That(exists, Is.True);
            }

            var req = handler.Requests.Single();
            Assert.That(req.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(req.RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/exists"));
            Assert.That(req.RequestUri.Query, Is.EqualTo("?hash=" + TestSha));
        }

        [Test]
        public async Task BlobExistsAsync_False_WhenCloudSaysAbsent()
        {
            var handler = new RecordingHandler();
            handler.QueueJson(HttpStatusCode.OK, "{\"exists\":false,\"size\":null}");
            using (var client = MakeClient(handler))
            {
                var exists = await client.BlobExistsAsync(TestSha);
                Assert.That(exists, Is.False);
            }
        }

        [Test]
        public void BlobExistsAsync_RejectsBadSha()
        {
            var handler = new RecordingHandler();
            using (var client = MakeClient(handler))
            {
                Assert.ThrowsAsync<ArgumentException>(() => client.BlobExistsAsync("not-hex-and-too-short"));
                Assert.ThrowsAsync<ArgumentException>(() => client.BlobExistsAsync("ABCDEF" + new string('0', 58)));
            }
            Assert.That(handler.Requests, Is.Empty, "bad-sha should short-circuit before any HTTP call");
        }

        [Test]
        public void BlobExistsAsync_ThrowsOn401_WithCloudMessage()
        {
            var handler = new RecordingHandler();
            handler.QueueJson(HttpStatusCode.Unauthorized, "{\"error\":\"Token revoked\",\"code\":\"auth\"}");
            using (var client = MakeClient(handler))
            {
                var ex = Assert.ThrowsAsync<PackRelayApiException>(() => client.BlobExistsAsync(TestSha));
                Assert.That(ex.StatusCode, Is.EqualTo(401));
                Assert.That(ex.Message, Is.EqualTo("Token revoked"));
                Assert.That(ex.ErrorCode, Is.EqualTo("auth"));
            }
        }

        [Test]
        public void BlobExistsAsync_NonJsonError_PassesThroughBody()
        {
            var handler = new RecordingHandler();
            handler.Queue(HttpStatusCode.BadGateway, "Upstream timeout", "text/plain");
            using (var client = MakeClient(handler))
            {
                var ex = Assert.ThrowsAsync<PackRelayApiException>(() => client.BlobExistsAsync(TestSha));
                Assert.That(ex.StatusCode, Is.EqualTo(502));
                Assert.That(ex.Message, Is.EqualTo("Upstream timeout"));
                Assert.That(ex.ErrorCode, Is.Null);
            }
        }

        // ---- Single-shot upload ----

        [Test]
        public async Task PutBlobAsync_SendsBytes_AtCorrectPathname()
        {
            var handler = new RecordingHandler();
            handler.QueueJson(HttpStatusCode.OK, "{\"ok\":true,\"pathname\":\"packs/files/...\",\"url\":\"...\",\"size\":5}");
            var payload = Encoding.UTF8.GetBytes("hello");
            using (var client = MakeClient(handler))
            using (var ms = new MemoryStream(payload))
            {
                await client.PutBlobAsync(TestSha, ms, payload.Length);
            }

            var req = handler.Requests.Single();
            Assert.That(req.Method, Is.EqualTo(HttpMethod.Put));
            Assert.That(req.RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/put"));
            // Pathname url-encoded; "/" -> "%2F"
            Assert.That(req.RequestUri.Query,
                Is.EqualTo("?pathname=packs%2Ffiles%2F" + TestSha));
            Assert.That(handler.RequestBodies.Single(), Is.EqualTo(payload));
            Assert.That(req.Content.Headers.ContentType.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(req.Content.Headers.ContentLength, Is.EqualTo(5));
        }

        [Test]
        public void PutBlobAsync_RejectsOversize()
        {
            var handler = new RecordingHandler();
            using (var client = MakeClient(handler))
            using (var ms = new MemoryStream(new byte[10]))
            {
                Assert.ThrowsAsync<ArgumentException>(() =>
                    client.PutBlobAsync(TestSha, ms, PackRelayClient.SingleShotMaxBytes + 1));
            }
            Assert.That(handler.Requests, Is.Empty);
        }

        // ---- Multipart ----

        [Test]
        public async Task MultipartInitAsync_PostsPathname_ReturnsUploadId()
        {
            var handler = new RecordingHandler();
            handler.QueueJson(HttpStatusCode.OK, "{\"uploadId\":\"upload-XYZ\",\"key\":\"packs/files/" + TestSha + "\"}");
            using (var client = MakeClient(handler))
            {
                var uploadId = await client.MultipartInitAsync(TestSha);
                Assert.That(uploadId, Is.EqualTo("upload-XYZ"));
            }
            var req = handler.Requests.Single();
            Assert.That(req.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(req.RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/multipart/init"));
            var body = Encoding.UTF8.GetString(handler.RequestBodies.Single());
            Assert.That(body, Does.Contain("\"pathname\":\"packs/files/" + TestSha + "\""));
        }

        [Test]
        public async Task MultipartPartAsync_PostsChunk_ReturnsEtag()
        {
            var handler = new RecordingHandler();
            handler.QueueJson(HttpStatusCode.OK, "{\"etag\":\"etag-1\"}");
            var chunk = new byte[100];
            for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)i;
            using (var client = MakeClient(handler))
            using (var ms = new MemoryStream(chunk))
            {
                var etag = await client.MultipartPartAsync(TestSha, "upload-XYZ", 1, ms, chunk.Length);
                Assert.That(etag, Is.EqualTo("etag-1"));
            }
            var req = handler.Requests.Single();
            Assert.That(req.RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/multipart/part"));
            Assert.That(req.RequestUri.Query, Does.Contain("uploadId=upload-XYZ"));
            Assert.That(req.RequestUri.Query, Does.Contain("partNumber=1"));
            Assert.That(handler.RequestBodies.Single(), Is.EqualTo(chunk));
        }

        [Test]
        public async Task MultipartCompleteAsync_PostsParts_InOrder()
        {
            var handler = new RecordingHandler();
            handler.QueueJson(HttpStatusCode.OK, "{\"ok\":true}");
            var parts = new[]
            {
                new MultipartPart("etag-1", 1),
                new MultipartPart("etag-2", 2),
            };
            using (var client = MakeClient(handler))
            {
                await client.MultipartCompleteAsync(TestSha, "upload-XYZ", parts);
            }
            var req = handler.Requests.Single();
            Assert.That(req.RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/multipart/complete"));
            var body = Encoding.UTF8.GetString(handler.RequestBodies.Single());
            // Both etags + part numbers should appear.
            Assert.That(body, Does.Contain("\"etag\":\"etag-1\""));
            Assert.That(body, Does.Contain("\"etag\":\"etag-2\""));
            Assert.That(body, Does.Contain("\"partNumber\":1"));
            Assert.That(body, Does.Contain("\"partNumber\":2"));
            Assert.That(body, Does.Contain("\"uploadId\":\"upload-XYZ\""));
        }

        // ---- UploadBlobAsync (size-agnostic) ----

        [Test]
        public async Task UploadBlobAsync_SmallFile_UsesSingleShot()
        {
            var handler = new RecordingHandler();
            // PUT response
            handler.QueueJson(HttpStatusCode.OK, "{\"ok\":true}");
            var bytes = new byte[1024]; // 1 KB
            using (var client = MakeClient(handler))
            using (var ms = new MemoryStream(bytes))
            {
                await client.UploadBlobAsync(TestSha, ms, bytes.Length);
            }
            var req = handler.Requests.Single();
            Assert.That(req.Method, Is.EqualTo(HttpMethod.Put));
            Assert.That(req.RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/put"));
        }

        [Test]
        public async Task UploadBlobAsync_LargeFile_UsesMultipart_InOrder()
        {
            // 10 MB with 4 MB chunks -> 3 parts (4 MB, 4 MB, 2 MB).
            var handler = new RecordingHandler();
            // init
            handler.QueueJson(HttpStatusCode.OK, "{\"uploadId\":\"u1\",\"key\":\"...\"}");
            // 3 parts
            handler.QueueJson(HttpStatusCode.OK, "{\"etag\":\"e1\"}");
            handler.QueueJson(HttpStatusCode.OK, "{\"etag\":\"e2\"}");
            handler.QueueJson(HttpStatusCode.OK, "{\"etag\":\"e3\"}");
            // complete
            handler.QueueJson(HttpStatusCode.OK, "{\"ok\":true}");

            var bytes = new byte[10 * 1024 * 1024];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 251);

            using (var client = MakeClient(handler))
            using (var ms = new MemoryStream(bytes))
            {
                // chunkBytes = 4 MB so the part boundaries are deterministic
                await client.UploadBlobAsync(TestSha, ms, bytes.Length, chunkBytes: 4 * 1024 * 1024);
            }

            // 5 requests: init, part1, part2, part3, complete
            Assert.That(handler.Requests.Count, Is.EqualTo(5));
            Assert.That(handler.Requests[0].RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/multipart/init"));
            Assert.That(handler.Requests[1].RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/multipart/part"));
            Assert.That(handler.Requests[2].RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/multipart/part"));
            Assert.That(handler.Requests[3].RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/multipart/part"));
            Assert.That(handler.Requests[4].RequestUri.AbsolutePath, Is.EqualTo("/api/v1/files/multipart/complete"));

            // Part numbers ascend.
            Assert.That(handler.Requests[1].RequestUri.Query, Does.Contain("partNumber=1"));
            Assert.That(handler.Requests[2].RequestUri.Query, Does.Contain("partNumber=2"));
            Assert.That(handler.Requests[3].RequestUri.Query, Does.Contain("partNumber=3"));

            // Last chunk is the 2 MB tail.
            Assert.That(handler.RequestBodies[1].Length, Is.EqualTo(4 * 1024 * 1024));
            Assert.That(handler.RequestBodies[2].Length, Is.EqualTo(4 * 1024 * 1024));
            Assert.That(handler.RequestBodies[3].Length, Is.EqualTo(2 * 1024 * 1024));

            // Chunks reassemble to the original.
            var reassembled = new byte[bytes.Length];
            Buffer.BlockCopy(handler.RequestBodies[1], 0, reassembled, 0, 4 * 1024 * 1024);
            Buffer.BlockCopy(handler.RequestBodies[2], 0, reassembled, 4 * 1024 * 1024, 4 * 1024 * 1024);
            Buffer.BlockCopy(handler.RequestBodies[3], 0, reassembled, 8 * 1024 * 1024, 2 * 1024 * 1024);
            Assert.That(reassembled, Is.EqualTo(bytes));

            // Complete body carries all 3 parts in order.
            var completeBody = Encoding.UTF8.GetString(handler.RequestBodies[4]);
            Assert.That(completeBody, Does.Contain("\"partNumber\":1"));
            Assert.That(completeBody, Does.Contain("\"partNumber\":2"));
            Assert.That(completeBody, Does.Contain("\"partNumber\":3"));
        }

        // ---- PublishVersion ----

        [Test]
        public async Task PublishVersionAsync_PostsRawJson_ToSlugUrl()
        {
            var handler = new RecordingHandler();
            handler.QueueJson(HttpStatusCode.Created, "{\"version\":\"1.2.3\"}");

            const string manifest = "{\"name\":\"test-pack\",\"version\":\"1.2.3\",\"signature\":{\"value\":\"...\"}}";
            using (var client = MakeClient(handler))
            {
                var response = await client.PublishVersionAsync("test-pack", manifest);
                Assert.That(response.Value<string>("version"), Is.EqualTo("1.2.3"));
            }

            var req = handler.Requests.Single();
            Assert.That(req.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(req.RequestUri.AbsolutePath, Is.EqualTo("/api/v1/packs/test-pack/versions"));
            Assert.That(req.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
            var body = Encoding.UTF8.GetString(handler.RequestBodies.Single());
            Assert.That(body, Is.EqualTo(manifest));
        }

        [Test]
        public void PublishVersionAsync_409Duplicate_SurfacesCloudCode()
        {
            // 409 from the cloud means "this version is already published".
            // The orchestrator treats that as a soft-success (idempotent
            // re-publish); the client itself just surfaces it.
            var handler = new RecordingHandler();
            handler.QueueJson(HttpStatusCode.Conflict,
                "{\"error\":\"Version 1.2.3 already published.\",\"code\":\"duplicate_version\"}");
            using (var client = MakeClient(handler))
            {
                var ex = Assert.ThrowsAsync<PackRelayApiException>(() =>
                    client.PublishVersionAsync("test-pack", "{\"name\":\"test-pack\"}"));
                Assert.That(ex.StatusCode, Is.EqualTo(409));
                Assert.That(ex.ErrorCode, Is.EqualTo("duplicate_version"));
            }
        }

        // ---- Plumbing ----

        private PackRelayClient MakeClient(RecordingHandler handler)
        {
            return new PackRelayClient(ApiBase, ApiToken, new HttpClient(handler));
        }

        /// <summary>
        /// Test HttpMessageHandler that records every request (including
        /// its body bytes — for assertion against payload contents)
        /// and replies with queued canned responses in FIFO order.
        /// </summary>
        private class RecordingHandler : HttpMessageHandler
        {
            public readonly List<HttpRequestMessage> Requests = new List<HttpRequestMessage>();
            public readonly List<byte[]> RequestBodies = new List<byte[]>();
            private readonly Queue<HttpResponseMessage> _responses = new Queue<HttpResponseMessage>();

            public void Queue(HttpStatusCode status, string body, string contentType)
            {
                var res = new HttpResponseMessage(status);
                if (body != null)
                {
                    res.Content = new StringContent(body, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(contentType))
                    {
                        res.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                    }
                }
                _responses.Enqueue(res);
            }

            public void QueueJson(HttpStatusCode status, string jsonBody) =>
                Queue(status, jsonBody, "application/json");

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // Capture body bytes BEFORE recording; the client may
                // dispose the request after returning, which disposes
                // its Content. We snapshot now so tests can assert.
                byte[] body = null;
                if (request.Content != null)
                {
                    body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
                Requests.Add(request);
                RequestBodies.Add(body ?? Array.Empty<byte>());
                if (_responses.Count == 0)
                {
                    throw new InvalidOperationException(
                        "RecordingHandler ran out of canned responses; got request " + Requests.Count);
                }
                return _responses.Dequeue();
            }
        }
    }
}
