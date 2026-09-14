import http from "node:http";
import { createHash } from "node:crypto";

const listenPort = Number(readArgument("--listen-port") || 5179);
const upstreamUrl = readArgument("--upstream") || "ws://127.0.0.1:5178";
if (!Number.isInteger(listenPort) || listenPort < 1024 || listenPort > 65535) throw new Error("Invalid loopback listen port.");
if (!/^ws:\/\/(?:127\.0\.0\.1|localhost):\d+\/?$/i.test(upstreamUrl)) throw new Error("The Codex upstream must be a loopback WebSocket URL.");

const server = http.createServer((request, response) => {
  response.writeHead(426, { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" });
  response.end("WebSocket upgrade required.");
});

server.on("upgrade", (request, socket, head) => {
  const origin = String(request.headers.origin || "");
  const key = String(request.headers["sec-websocket-key"] || "");
  if (!/^http:\/\/(?:127\.0\.0\.1|localhost):\d+$/i.test(origin) || !key) {
    socket.end("HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n");
    return;
  }

  const upstream = new WebSocket(upstreamUrl);
  const timeout = setTimeout(() => socket.destroy(), 5000);
  let upgraded = false;
  let incoming = Buffer.alloc(0);
  let fragmentedOpcode = 0;
  let fragments = [];

  upstream.addEventListener("open", () => {
    clearTimeout(timeout);
    const accept = createHash("sha1").update(`${key}258EAFA5-E914-47DA-95CA-C5AB0DC85B11`).digest("base64");
    socket.write([
      "HTTP/1.1 101 Switching Protocols",
      "Upgrade: websocket",
      "Connection: Upgrade",
      `Sec-WebSocket-Accept: ${accept}`,
      "\r\n"
    ].join("\r\n"));
    upgraded = true;
    if (head?.length) consume(head);
    socket.on("data", consume);
  });

  upstream.addEventListener("message", async (event) => {
    if (!upgraded || socket.destroyed) return;
    const isText = typeof event.data === "string";
    const payload = isText ? Buffer.from(event.data, "utf8") : Buffer.from(await event.data.arrayBuffer?.() ?? event.data);
    socket.write(encodeFrame(payload, isText ? 0x1 : 0x2));
  });
  upstream.addEventListener("close", (event) => {
    clearTimeout(timeout);
    if (upgraded && !socket.destroyed) socket.end(encodeCloseFrame(event.code, event.reason));
    else socket.destroy();
  });
  upstream.addEventListener("error", () => {
    clearTimeout(timeout);
    if (!upgraded && !socket.destroyed) socket.end("HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n");
    else socket.destroy();
  });
  socket.on("close", () => { if (upstream.readyState <= WebSocket.OPEN) upstream.close(); });
  socket.on("error", () => { if (upstream.readyState <= WebSocket.OPEN) upstream.close(); });

  function consume(chunk) {
    incoming = Buffer.concat([incoming, chunk]);
    while (true) {
      const frame = decodeFrame(incoming);
      if (!frame) return;
      incoming = incoming.subarray(frame.consumed);
      if (frame.opcode === 0x8) {
        upstream.close();
        return;
      }
      if (frame.opcode === 0x9) {
        socket.write(encodeFrame(frame.payload, 0xA));
        continue;
      }
      if (frame.opcode === 0xA) continue;
      if (frame.opcode === 0x1 || frame.opcode === 0x2) {
        if (frame.fin) upstream.send(frame.opcode === 0x1 ? frame.payload.toString("utf8") : frame.payload);
        else {
          fragmentedOpcode = frame.opcode;
          fragments = [frame.payload];
        }
        continue;
      }
      if (frame.opcode === 0x0 && fragmentedOpcode) {
        fragments.push(frame.payload);
        if (frame.fin) {
          const payload = Buffer.concat(fragments);
          upstream.send(fragmentedOpcode === 0x1 ? payload.toString("utf8") : payload);
          fragmentedOpcode = 0;
          fragments = [];
        }
      }
    }
  }
});

server.listen(listenPort, "127.0.0.1");

function readArgument(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] || "" : "";
}

function decodeFrame(buffer) {
  if (buffer.length < 2) return null;
  const first = buffer[0];
  const second = buffer[1];
  let length = second & 0x7F;
  let offset = 2;
  if (length === 126) {
    if (buffer.length < 4) return null;
    length = buffer.readUInt16BE(2);
    offset = 4;
  } else if (length === 127) {
    if (buffer.length < 10) return null;
    const wide = buffer.readBigUInt64BE(2);
    if (wide > BigInt(Number.MAX_SAFE_INTEGER)) throw new Error("WebSocket frame is too large.");
    length = Number(wide);
    offset = 10;
  }
  const masked = Boolean(second & 0x80);
  const maskBytes = masked ? 4 : 0;
  if (buffer.length < offset + maskBytes + length) return null;
  const mask = masked ? buffer.subarray(offset, offset + 4) : null;
  offset += maskBytes;
  const payload = Buffer.from(buffer.subarray(offset, offset + length));
  if (mask) for (let index = 0; index < payload.length; index += 1) payload[index] ^= mask[index % 4];
  return { fin: Boolean(first & 0x80), opcode: first & 0x0F, payload, consumed: offset + length };
}

function encodeFrame(payload, opcode) {
  const length = payload.length;
  let header;
  if (length < 126) {
    header = Buffer.from([0x80 | opcode, length]);
  } else if (length <= 0xFFFF) {
    header = Buffer.alloc(4);
    header[0] = 0x80 | opcode;
    header[1] = 126;
    header.writeUInt16BE(length, 2);
  } else {
    header = Buffer.alloc(10);
    header[0] = 0x80 | opcode;
    header[1] = 127;
    header.writeBigUInt64BE(BigInt(length), 2);
  }
  return Buffer.concat([header, payload]);
}

function encodeCloseFrame(code = 1000, reason = "") {
  const reasonBytes = Buffer.from(String(reason || ""), "utf8").subarray(0, 123);
  const payload = Buffer.alloc(2 + reasonBytes.length);
  payload.writeUInt16BE(code || 1000, 0);
  reasonBytes.copy(payload, 2);
  return encodeFrame(payload, 0x8);
}
