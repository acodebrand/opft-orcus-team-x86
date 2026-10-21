import ast
import asyncio
import copy
import time
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock

PATH = Path(r'D:\descargas\main01 (39) - envios rapidos.py')
tree = ast.parse(PATH.read_text(encoding='utf-8'))
names = {'_fast_send_is_on', '_fast_send_set', '_send_mode_delay',
         '_handle_fast_send_callback', 'enviar_spam'}
nodes = [copy.deepcopy(n) for n in ast.walk(tree)
         if isinstance(n, (ast.FunctionDef, ast.AsyncFunctionDef)) and n.name in names]
callback = next(n for n in ast.walk(tree) if isinstance(n, ast.AsyncFunctionDef) and n.name == 'callback')
manual = next(n for n in callback.body if isinstance(n, ast.If)
              and ast.unparse(n.test) == "data.startswith('msg_')")
wrapper = ast.parse('async def manual_send(event, client_user, user_id, uid_int, saved_messages, data):\n    pass').body[0]
wrapper.body = [copy.deepcopy(manual)]
nodes.append(wrapper)
module = ast.fix_missing_locations(ast.Module(body=nodes, type_ignores=[]))
code = compile(module, '<isolated sending functions>', 'exec')

class Event:
    sender_id = 7
    chat_id = 7
    message_id = 100
    is_private = True
    def __init__(self):
        self.answer = AsyncMock()
        self.edit = AsyncMock()

class Tests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.disk = {}
        self.delays = []
        self.sent = []
        self.bot = SimpleNamespace(_manual_stop={}, send_message=AsyncMock())
        async def sleep_delay(a, b):
            self.delays.append((a, b))
        async def send(client, entity, msg):
            self.sent.append(entity)
            return True, 'ok'
        async def finish_cycle(*args, **kwargs):
            raise asyncio.CancelledError
        self.g = dict(asyncio=asyncio, time=time, FAST_SEND_FILE='modes',
            _fast_send_state=None, _fast_send_pending={},
            _load_json=lambda p, default: dict(self.disk.get(p, default)),
            _save_json=lambda p, value: self.disk.update({p: dict(value)}),
            _sleep_random_range=sleep_delay,
            _ensure_active=AsyncMock(return_value=True), mostrar_menu=AsyncMock(),
            Button=SimpleNamespace(inline=lambda text, data: (text, data)),
            bot=self.bot, _safe_send_like_human=send,
            _bl_is_blocked=lambda uid, gid: gid == 2,
            _af_is_on=lambda uid: True, GROUP_DELAY_MIN_S=15,
            GROUP_DELAY_MAX_S=28, BATCH_DELAY_MIN_S=90, BATCH_DELAY_MAX_S=180,
            MIN_PROGRAM_INTERVAL_MIN=130, MIN_PROGRAM_INTERVAL_SEC=7800,
            _clamp_program_interval=lambda value: max(7800, int(value)),
            _has_active_or_admin=lambda uid: True,
            _guardar_spams_to_file=lambda: None, spam_tasks={},
            _ahora_ts=lambda: 1000, _tick_sleep_update=finish_cycle,
            FLOOD_WAIT_EXTRA_BUFFER_S=30, ACCOUNT_BLOCK_PEERFLOOD_S=21600,
            SHOW_SPAM_STATUS_UI=False, SHOW_MENU_AFTER_EACH_SEND=False)
        exec(code, self.g)
        async def dialogs():
            for i in range(12):
                yield SimpleNamespace(id=i, entity=SimpleNamespace(megagroup=True, id=i))
        self.client = SimpleNamespace(iter_dialogs=dialogs)
        self.event = Event()
        self.msg = SimpleNamespace(raw_text='prueba')

    async def press(self, data, event=None):
        return await self.g['_handle_fast_send_callback'](event or self.event, data, self.client, '7')

    async def run_send(self, fast=True, scheduled=False):
        self.g['_fast_send_set'](7, fast)
        if scheduled:
            with self.assertRaises(asyncio.CancelledError):
                await self.g['enviar_spam'](self.client, self.msg, 7800, '7', self.event, 0)
        else:
            await self.g['manual_send'](self.event, self.client, '7', 7, [self.msg], 'msg_0')

    async def test_confirmation_activation_disable(self):
        self.assertFalse(self.g['_fast_send_is_on'](7))
        await self.press('fast_send_menu')
        self.assertFalse(self.g['_fast_send_is_on'](7))
        self.assertTrue(self.event.answer.call_args.kwargs['alert'])
        self.assertLessEqual(len(self.event.answer.call_args.args[0]), 200)
        await self.press('fast_send_accept')
        self.assertTrue(self.g['_fast_send_is_on'](7))
        self.assertFalse(self.g['_fast_send_is_on'](8))
        await self.press('fast_send_menu')
        self.assertFalse(self.g['_fast_send_is_on'](7))

    async def test_cancel(self):
        await self.press('fast_send_menu')
        await self.press('fast_send_cancel')
        self.assertFalse(self.g['_fast_send_is_on'](7))

    async def test_no_confirmation_no_activation(self):
        await self.press('fast_send_accept')
        self.assertFalse(self.g['_fast_send_is_on'](7))

    async def test_wrong_message_and_expired(self):
        await self.press('fast_send_menu')
        other = Event()
        other.message_id = 101
        await self.press('fast_send_accept', other)
        self.assertFalse(self.g['_fast_send_is_on'](7))
        key, expiry = self.g['_fast_send_pending'][7]
        self.g['_fast_send_pending'][7] = (key, 0)
        await self.press('fast_send_accept')
        self.assertFalse(self.g['_fast_send_is_on'](7))

    async def test_private_and_subscription_guards(self):
        self.event.is_private = False
        await self.press('fast_send_menu')
        self.assertFalse(self.g['_fast_send_pending'])
        self.event.is_private = True
        self.g['_ensure_active'].return_value = False
        await self.press('fast_send_menu')
        self.assertFalse(self.g['_fast_send_pending'])

    async def test_persistence(self):
        self.g['_fast_send_set'](7, True)
        self.g['_fast_send_state'] = None
        self.assertTrue(self.g['_fast_send_is_on'](7))

    async def test_manual_fast_and_blacklist(self):
        await self.run_send()
        self.assertEqual(len(self.sent), 11)
        self.assertNotIn(2, [e.id for e in self.sent])
        self.assertEqual(self.delays, [])

    async def test_manual_normal_delays(self):
        await self.run_send(fast=False)
        self.assertEqual(len(self.sent), 11)
        self.assertEqual(self.delays.count((15, 28)), 8)
        self.assertEqual(self.delays.count((90, 180)), 2)

    async def test_scheduled_fast(self):
        await self.run_send(scheduled=True)
        self.assertEqual(len(self.sent), 11)
        self.assertEqual(self.delays, [])
        self.assertEqual(self.g['spam_tasks']['7'][0]['next_run_ts'], 8800)

    async def test_scheduled_normal(self):
        await self.run_send(fast=False, scheduled=True)
        self.assertEqual(len(self.delays), 10)

    async def test_manual_flood_and_peerflood_stop(self):
        for reason in ['floodwait:240s', 'peerflood']:
            send = AsyncMock(return_value=(False, reason))
            self.g['_safe_send_like_human'] = send
            await self.run_send()
            self.assertEqual(send.await_count, 1)

    async def test_scheduled_flood_cooldown(self):
        send = AsyncMock(return_value=(False, 'floodwait:10000s'))
        self.g['_safe_send_like_human'] = send
        await self.run_send(scheduled=True)
        self.assertEqual(send.await_count, 1)
        self.assertEqual(self.g['spam_tasks']['7'][0]['next_run_ts'], 11030)

    async def test_manual_stop(self):
        async def send(*args):
            self.bot._manual_stop[7] = True
            self.sent.append(1)
            return True, 'ok'
        self.g['_safe_send_like_human'] = send
        await self.run_send()
        self.assertEqual(len(self.sent), 1)

    async def test_missing_message(self):
        await self.g['manual_send'](self.event, self.client, '7', 7, [], 'msg_0')
        self.assertEqual(self.sent, [])

if __name__ == '__main__':
    unittest.main(verbosity=2)
