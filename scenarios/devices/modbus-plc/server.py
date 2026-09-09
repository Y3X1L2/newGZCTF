"""Stateful Modbus TCP training device; the protocol implementation is pymodbus."""
import asyncio
import json
import os
import uuid

from pymodbus.datastore import ModbusDeviceContext, ModbusSequentialDataBlock, ModbusServerContext
from pymodbus.server import StartAsyncTcpServer
from pymodbus.constants import ExcCodes
from pymodbus.pdu import ExceptionResponse
from pymodbus.pdu.bit_message import WriteSingleCoilRequest


class DeviceContext(ModbusDeviceContext):
    def __init__(self, **kwargs):
        super().__init__(**kwargs)
        self.boot_id = str(uuid.uuid4())
        self.counters = {"modbus.read": 0, "modbus.write": 0}

    async def async_getValues(self, func_code, address, count=1):
        limit = 2000 if func_code in (1, 2) else 125
        if not 1 <= count <= limit:
            return ExcCodes.ILLEGAL_VALUE
        result = await super().async_getValues(func_code, address, count)
        if not isinstance(result, ExcCodes) and func_code in (1, 2, 3, 4):
            self.counters["modbus.read"] += 1
        return result

    async def async_setValues(self, func_code, address, values):
        limit = 1968 if func_code == 15 else 123 if func_code == 16 else 1
        if not 1 <= len(values) <= limit:
            return ExcCodes.ILLEGAL_VALUE
        result = await super().async_setValues(func_code, address, values)
        if result is None:
            self.counters["modbus.write"] += 1
        return result


class WriteCoilRequest(WriteSingleCoilRequest):
    def decode(self, data):
        super().decode(data)
        self.invalid_value = data[2:4] not in (b"\x00\x00", b"\xff\x00")

    async def update_datastore(self, context):
        if self.invalid_value:
            return ExceptionResponse(self.function_code, ExcCodes.ILLEGAL_VALUE)
        return await super().update_datastore(context)


def parameters():
    raw = os.environ.get("GZCTF_DEVICE_PARAMETERS", "{}")
    if len(raw) > 2048:
        raise ValueError("device parameters exceed 2048 characters")
    values = json.loads(raw)
    if not isinstance(values, dict) or values.keys() - {"unitId", "port", "holdingRegisters"}:
        raise ValueError("unknown device parameters")
    unit = values.get("unitId", 1)
    port = values.get("port", 1502)
    registers = values.get("holdingRegisters", [0] * 32)
    if type(unit) is not int or not 1 <= unit <= 247:
        raise ValueError("unitId must be an integer from 1 to 247")
    if type(port) is not int or not 1024 <= port <= 65535:
        raise ValueError("port must be an integer from 1024 to 65535")
    if not isinstance(registers, list) or not 1 <= len(registers) <= 128 or any(
        type(value) is not int or not 0 <= value <= 65535 for value in registers
    ):
        raise ValueError("holdingRegisters must contain 1-128 unsigned 16-bit integers")
    return unit, port, registers


async def main():
    unit, port, registers = parameters()
    # ModbusDeviceContext offsets wire addresses by one. Wire register 0 maps to block address 1.
    device = DeviceContext(
        hr=ModbusSequentialDataBlock(1, registers),
        ir=ModbusSequentialDataBlock(1, [0] * 32),
        co=ModbusSequentialDataBlock(1, [False] * 32),
        di=ModbusSequentialDataBlock(1, [False] * 32),
    )
    context = ModbusServerContext(devices={unit: device}, single=False)

    async def health(reader, writer):
        try:
            request = await asyncio.wait_for(reader.readuntil(b"\r\n\r\n"), timeout=3)
            if not request.startswith((b"GET /health HTTP/1.0\r\n", b"GET /health HTTP/1.1\r\n")):
                writer.write(b"HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n")
            else:
                body = json.dumps({"bootId": device.boot_id, "counters": device.counters}).encode()
                writer.write(b"HTTP/1.0 200 OK\r\nContent-Type: application/json\r\nContent-Length: " +
                             str(len(body)).encode() + b"\r\nConnection: close\r\n\r\n" + body)
            await writer.drain()
        except (TimeoutError, asyncio.IncompleteReadError, asyncio.LimitOverrunError, ConnectionError):
            pass
        finally:
            writer.close()
            await writer.wait_closed()

    # Health and counters share the protocol event loop; an unresponsive device cannot report fresh health.
    health_server = await asyncio.start_server(health, "0.0.0.0", 1503, limit=2048)
    async with health_server:
        await StartAsyncTcpServer(context=context, address=("0.0.0.0", port), custom_pdu=[WriteCoilRequest])


if __name__ == "__main__":
    asyncio.run(main())
