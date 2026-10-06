#!/usr/bin/env python3
"""Regenerates mixed.pcap: synthetic packets only (RFC 5737 / RFC 3849 documentation addresses), no real traffic.

Contents, in order: TCP v4 handshake + data (192.0.2.1:1234 -> 192.0.2.2:443), UDP on VLAN 42, TCP v6 SYN, ICMP, ARP.
Usage: python3 make_mixed_pcap.py mixed.pcap
"""
import socket
import struct
import sys


def mac(text): return bytes(int(part, 16) for part in text.split(':'))


def checksum(data):
    if len(data) % 2: data += b'\0'
    total = sum(struct.unpack('!%dH' % (len(data) // 2), data))
    total = (total >> 16) + (total & 0xffff)
    total += total >> 16
    return ~total & 0xffff


def ethernet(dst, src, ethertype, vlan=None):
    if vlan is None: return mac(dst) + mac(src) + struct.pack('!H', ethertype)
    return mac(dst) + mac(src) + struct.pack('!HHH', 0x8100, vlan, ethertype)


def ipv4(src, dst, proto, payload):
    header = struct.pack('!BBHHHBBH4s4s', 0x45, 0, 20 + len(payload), 1, 0, 64, proto, 0, socket.inet_aton(src), socket.inet_aton(dst))
    return header[:10] + struct.pack('!H', checksum(header)) + header[12:] + payload


def tcp(sport, dport, flags, payload=b''):
    return struct.pack('!HHIIBBHHH', sport, dport, 1000, 0, 5 << 4, flags, 8192, 0, 0) + payload


def udp(sport, dport, payload=b''): return struct.pack('!HHHH', sport, dport, 8 + len(payload), 0) + payload


def ipv6(src, dst, nxt, payload):
    return struct.pack('!IHBB16s16s', 0x60000000, len(payload), nxt, 64, socket.inet_pton(socket.AF_INET6, src), socket.inet_pton(socket.AF_INET6, dst)) + payload


A, B, START = 'aa:bb:cc:00:00:01', 'aa:bb:cc:00:00:02', 1790000000.100
packets = [
    (0.0, ethernet(B, A, 0x800) + ipv4('192.0.2.1', '192.0.2.2', 6, tcp(1234, 443, 0x02))),
    (0.1, ethernet(A, B, 0x800) + ipv4('192.0.2.2', '192.0.2.1', 6, tcp(443, 1234, 0x12))),
    (0.2, ethernet(B, A, 0x800) + ipv4('192.0.2.1', '192.0.2.2', 6, tcp(1234, 443, 0x10))),
    (0.3, ethernet(B, A, 0x800) + ipv4('192.0.2.1', '192.0.2.2', 6, tcp(1234, 443, 0x18, b'hello'))),
    (0.4, ethernet(B, A, 0x800, vlan=42) + ipv4('198.51.100.5', '198.51.100.6', 17, udp(5353, 53, b'q'))),
    (0.5, ethernet(B, A, 0x86dd) + ipv6('2001:db8::1', '2001:db8::2', 6, tcp(40000, 80, 0x02))),
    (0.6, ethernet(B, A, 0x800) + ipv4('192.0.2.1', '192.0.2.2', 1, b'\x08\x00\x00\x00\x00\x01\x00\x01')),
    (0.7, ethernet('ff:ff:ff:ff:ff:ff', A, 0x806) + b'\x00\x01\x08\x00\x06\x04\x00\x01' + mac(A) + socket.inet_aton('192.0.2.1') + b'\0' * 6 + socket.inet_aton('192.0.2.2')),
]
with open(sys.argv[1], 'wb') as output:
    output.write(struct.pack('<IHHiIII', 0xa1b2c3d4, 2, 4, 0, 0, 65535, 1))
    for offset, frame in packets:
        stamp = START + offset
        seconds = int(stamp)
        output.write(struct.pack('<IIII', seconds, int(round((stamp - seconds) * 1e6)), len(frame), len(frame)) + frame)
