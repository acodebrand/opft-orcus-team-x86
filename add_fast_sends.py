from pathlib import Path
import ast

src = Path(r'D:\descargas\main01 (39).py')
dst = Path(r'D:\descargas\main01 (39) - envios rapidos.py')
original = src.read_bytes()
s = original.decode('utf-8')
newline = '\r\n' if '\r\n' in s else '\n'
s = s.replace('\r\n', '\n')

def replace(old, new, count=1):
    global s
    assert s.count(old) == count, (old[:100], s.count(old))
    s = s.replace(old, new)

replace('def _load_af_map():', '''# Envíos rápidos: preferencia independiente por usuario, desactivada por defecto.
FAST_SEND_FILE = "envios_rapidos.json"
_fast_send_state = None
_fast_send_pending = {}

def _fast_send_is_on(uid_int):
    global _fast_send_state
    if _fast_send_state is None:
        _fast_send_state = _load_json(FAST_SEND_FILE, {})
        if not isinstance(_fast_send_state, dict):
            _fast_send_state = {}
    return _fast_send_state.get(str(uid_int)) is True

def _fast_send_set(uid_int, enabled):
    _fast_send_is_on(uid_int)
    _fast_send_state[str(uid_int)] = bool(enabled)
    _save_json(FAST_SEND_FILE, _fast_send_state)

async def _send_mode_delay(uid_int, low, high):
    if _fast_send_is_on(uid_int):
        # Cede el control para que Detener siga respondiendo, sin pausa artificial.
        await asyncio.sleep(0)
    else:
        await _sleep_random_range(low, high)

def _load_af_map():''')

replace('        actions = [\n            Button.inline("⏰', '''        actions = [
            Button.inline(f"Envíos rápidos: {'ON' if _fast_send_is_on(uid_int) else 'OFF'}", data="fast_send_menu"),
            Button.inline("⏰''')

anchor = '    def _main_menu_buttons(saved_messages):'
handler = '''    async def _handle_fast_send_callback(event, data, client_user, user_id):
        if data not in {"fast_send_menu", "fast_send_accept", "fast_send_cancel"}:
            return False
        uid_int = int(event.sender_id)
        if not event.is_private:
            await event.answer("Configura Envíos rápidos por privado con el bot.", alert=True)
            return True
        if not await _ensure_active(event, uid_int):
            return True
        if not client_user:
            await event.answer("Entra con /start primero.", alert=True)
            return True
        pending_key = (uid_int, event.chat_id, event.message_id)
        if data == "fast_send_menu":
            if _fast_send_is_on(uid_int):
                _fast_send_pending.pop(uid_int, None)
                _fast_send_set(uid_int, False)
                await event.answer("Envíos rápidos desactivados.")
                await mostrar_menu(event, client_user, user_id)
                return True
            warning = (
                "Riesgo de FloodWait o restricciones. El nivel de riesgo depende de Telegram; "
                "no se garantiza menos de 2 minutos. Para activar, pulsa Aceptar y activar en el mensaje."
            )
            await event.answer(warning, alert=True)
            await event.edit(
                "**Envíos rápidos**\\n\\n"
                "Elimina las pausas entre grupos y lotes en tus envíos manuales y programados. "
                "Mantiene el intervalo programado y los grupos excluidos.\\n\\n"
                "**Riesgo:** pueden aparecer FloodWait o restricciones de Telegram. "
                "No se puede garantizar un riesgo bajo ni completar todos los grupos en menos de 2 minutos. "
                "Si Telegram exige esperar, se respeta el bloqueo.\\n\\n"
                "Pulsa **Aceptar y activar** para habilitarlo; después elige tu mensaje.",
                buttons=[
                    [Button.inline("Aceptar y activar", data="fast_send_accept")],
                    [Button.inline("Cancelar", data="fast_send_cancel")],
                ], parse_mode="md"
            )
            _fast_send_pending[uid_int] = (pending_key, time.monotonic() + 300)
            return True
        pending = _fast_send_pending.get(uid_int)
        if not pending or pending[0] != pending_key or pending[1] < time.monotonic():
            await event.answer("Esta confirmación venció. Abre Envíos rápidos de nuevo.", alert=True)
            return True
        _fast_send_pending.pop(uid_int, None)
        if data == "fast_send_accept":
            _fast_send_set(uid_int, True)
            await event.answer("Envíos rápidos activados.")
        else:
            await event.answer("Activación cancelada.")
        await mostrar_menu(event, client_user, user_id)
        return True

'''
replace(anchor, handler + anchor)
replace('        # === BÚSQUEDA DE USERNAMES / CRÉDITOS ===', '''        if await _handle_fast_send_callback(event, data, client_user, user_id):
            return

        # === BÚSQUEDA DE USERNAMES / CRÉDITOS ===''')
replace('await _sleep_random_range(GROUP_DELAY_MIN_S, GROUP_DELAY_MAX_S)',
        'await _send_mode_delay(uid_int, GROUP_DELAY_MIN_S, GROUP_DELAY_MAX_S)', count=2)
replace('await _sleep_random_range(BATCH_DELAY_MIN_S, BATCH_DELAY_MAX_S)',
        'await _send_mode_delay(uid_int, BATCH_DELAY_MIN_S, BATCH_DELAY_MAX_S)', count=2)
# Evita continuar el envío con un mensaje inexistente.
replace('                await event.answer("⚠️ Ese mensaje ya no está disponible. Abre el menu para actualizar", alert=True)\n',
        '                await event.answer("⚠️ Ese mensaje ya no está disponible. Abre el menu para actualizar", alert=True)\n                return\n')
ast.parse(s)
compile(s, str(dst), 'exec')
dst.write_bytes(s.replace('\n', newline).encode('utf-8'))
assert src.read_bytes() == original
print(f'Archivo completo creado: {dst}')
print('Sintaxis correcta. Original intacto.')
