import random
from rest_framework.response import Response
from ..ai_core.session_manager import reset_session_completely, reset_pending
from .naming_engine import (
    SCHEMES,
    resolve_scheme_for_request,
    _detect_scheme_from_sheets,
)


def _scheme_label(scheme_name, thai=False):
    """User-facing label for an internal scheme key.

    Internal keys: `iso19650_4digit`, `iso19650_5digit`, `a49_dotted`.
    Legacy keys (`v1_small`, `v2_large`) from earlier session storage are
    auto-migrated by `resolve_scheme_for_request()`.
    """
    if scheme_name == "a49_dotted":
        return "a49SheetNaming (A1.03 dotted)" if not thai else "a49SheetNaming (A1.03 แบบจุด)"
    digit_count = SCHEMES.get(scheme_name, {}).get("digit_count", "?")
    if thai:
        return f"ISO19650 {digit_count} หลัก"
    return f"ISO19650 {digit_count}-digit"

def process_conversational_intent(raw_text_lower, request):
    """
    Intercepts basic conversational intents (Greetings, Thank You, Help, Reset)
    and returns a language-aware Response if matched, otherwise returns None.
    """
    clean_text = raw_text_lower.strip(".!, ?")
    
    # Simple language detector (Checks if there are any Thai characters in the string)
    is_thai = any('\u0E00' <= c <= '\u0E7F' for c in clean_text)

    # ==========================================================
    # 1. GREETINGS
    # ==========================================================
    greetings = {
        "hi", "hello", "good morning", "good afternoon", "good evening", "hey", 
        "hi vella", "hello vella", "hey vella", "greetings", "yo", "sup", "what's up",
        "สวัสดี", "หวัดดี", "ดีจ้า", "สวัสดีค่ะ", "สวัสดีครับ", "ทักทาย", "ฮัลโหล", "ดีครับ", "ดีค่ะ", "หวัดดี vella"
    }
    
    if clean_text in greetings or raw_text_lower.replace(".", "") in greetings:
        reset_pending(request)
        
        if is_thai:
            variants = [
                "สวัสดีค่ะ Vella พร้อมช่วยงาน Revit แล้ว วันนี้ให้ช่วยอะไรดีคะ?",
                "สวัสดีค่ะ! มีโมเดลไหนให้ Vella ช่วยจัดการบ้างคะ บอกได้เลย",
                "ทักทายค่ะ! วันนี้จะทำ Sheet หรือสร้าง View ใหม่ดีคะ?",
                "สวัสดีจ้า พร้อมลุยงานดราฟต์แล้ว สั่งมาได้เลยค่ะ!"
            ]
        else:
            variants = [
                "Hi there! How can I assist you with your Revit model today?",
                "Hello! I'm Vella, ready to tackle some drafting. What's on the agenda?",
                "Good to see you! Do you need help creating views or placing sheets?",
                "Hey! Ready when you are. Just let me know what Revit tasks we're doing today."
            ]
            
        return Response({"message": random.choice(variants), "session_key": request.session.session_key})

    # ==========================================================
    # 2. THANK YOU / PRAISE
    # ==========================================================
    thank_yous = {
        "thank you", "thanks", "thx", "thank you vella", "thanks vella", "appreciate it", 
        "awesome", "great job", "good job", "perfect", "nice", "cool",
        "ขอบคุณ", "ขอบคุณค่ะ", "ขอบคุณครับ", "ขอบใจ", "แต๊งกิ้ว", "เยี่ยมเลย", "ดีมาก", "สุดยอด", "เก่งมาก"
    }
    
    if clean_text in thank_yous:
        if is_thai:
            variants = [
                "ยินดีเสมอค่ะ! มีอะไรให้ช่วยดราฟต์อีกบอก Vella ได้ตลอดเลยนะคะ",
                "ด้วยความยินดีค่ะ! ดีใจที่ได้ช่วยลดเวลางานให้นะคะ",
                "ไม่เป็นไรเลยค่ะ งาน Revit ยกให้ Vella จัดการได้เลย!",
                "ยินดีให้บริการค่ะ ลุยงานกันต่อเลยไหมคะ?"
            ]
        else:
            variants = [
                "You're very welcome! Let me know if you need anything else.",
                "Glad I could help! That's what I'm here for.",
                "Anytime! My Revit engines are always ready for more.",
                "Happy to assist! We make a great team."
            ]
            
        return Response({"message": random.choice(variants), "session_key": request.session.session_key})

    # ==========================================================
    # 3. HELP / DOCUMENTATION
    # ==========================================================
    helps = {
        "help", "help me", "support", "manual", "instructions", "info", "how to use", 
        "what can you do", "guide", "tutorial",
        "ช่วยด้วย", "ขอความช่วยเหลือ", "ใช้งานยังไง", "ทำอะไรได้บ้าง", "คู่มือ", "สอนหน่อย"
    }
    
    if clean_text in helps:
        if is_thai:
            msg = (
                "ต้องการคำแนะนำใช่ไหมคะ? 💡\n\n"
                "ลองกดที่ปุ่ม **Help / Documentation** (ไอคอนมุมขวาบน) เพื่อดูคู่มือการใช้งานและคำสั่งทั้งหมดของ Vella ได้เลยค่ะ\n\n"
                "แต่ถ้ายังมีข้อสงสัยหรือเจอปัญหาการใช้งาน สามารถติดต่อทีม **IRIs Department** ได้เลยนะคะ พวกเขาพร้อมช่วยเหลือเสมอค่ะ!"
            )
        else:
            msg = (
                "Need some guidance? 💡\n\n"
                "You can click the **Help / Documentation** icon in the top corner of the window to see my full manual and a list of things I can do.\n\n"
                "If you still need assistance or run into any bugs, please reach out to the **IRIs Department**!"
            )
            
        return Response({"message": msg, "session_key": request.session.session_key})

    # ==========================================================
    # 4. MATH / CALCULATOR CAPABILITIES
    # ==========================================================
    math_inquiries = {
        # English
        "math", "calculator", "what can you calculate", "math help",
        "can you help me with a math solution", "how to calculate",
        "conversion help", "unit conversion", "math capabilities", "help me with math",
        # Thai
        "คิดเลข", "เครื่องคิดเลข", "คณิตศาสตร์", "คำนวณอะไรได้บ้าง", 
        "ช่วยคิดเลขหน่อย", "ช่วยคำนวณหน่อย", "แปลงหน่วย", "ช่วยแปลงหน่วย", 
        "คำนวณให้หน่อย", "คิดเลขให้หน่อย", "ทำเลขได้ไหม", "วิธีคำนวณ"
    }
    
    # Intercept queries asking ABOUT math (but ignore actual math equations)
    if (clean_text in math_inquiries or 
        clean_text.startswith("can you help me with math") or 
        clean_text.startswith("do you do math") or
        clean_text.startswith("ช่วยคำนวณ") or
        clean_text.startswith("ช่วยคิดเลข")):
        
        if is_thai:
            msg = (
                "Vella มีระบบเครื่องคิดเลขสำหรับสถาปนิกในตัวค่ะ! นี่คือสิ่งที่ Vella คำนวณให้ได้:\n\n"
                "• **ความลาดชัน (Slope/Ramp):** พิมพ์ `Calculate slope with a rise of 150 and a run of 2000`\n"
                "• **หาพื้นที่ (Area):** พิมพ์ `Area of 5000 mm by 4000 mm`\n"
                "• **แปลงหน่วยสากล:** พิมพ์ `Convert 1500 sqft to sqm` หรือ `Convert 500 mm to inches`\n"
                "• **แปลงหน่วยพื้นที่ไทย:** พิมพ์ `Convert 5 rai to sqm` หรือ `Format 2000 sqm in thai units`\n"
                "• **คำนวณคณิตศาสตร์ทั่วไป:** พิมพ์ `Calculate (1500 + 200) * 4.5`\n\n"
                "พิมพ์สมการหรือสิ่งที่ต้องการแปลงลงในแชทได้เลยค่ะ!"
            )
        else:
            msg = (
                "I am equipped with a built-in architectural calculator! Here is what I can do for you:\n\n"
                "• **Slope & Ramps:** Type `Calculate slope with a rise of 150 and a run of 2000`\n"
                "• **Area & Dimensions:** Type `Area of 5000 mm by 4000 mm`\n"
                "• **Standard Conversions:** Type `Convert 1500 sqft to sqm` or `Convert 500 mm to inches`\n"
                "• **Thai Land Units:** Type `Convert 5 rai to sqm` or `Format 2000 sqm in thai units`\n"
                "• **General Math:** Type `Calculate (1500 + 200) * 4.5`\n\n"
                "Just type your equation or conversion directly into the chat!"
            )
        return Response({"message": msg, "session_key": request.session.session_key})
     
    # ==========================================================
    # 4. RESET / CLEAR
    # ==========================================================
    resets = {
        "reset", "clear", "clear memory", "start over", "new session",
        "forget everything", "รีเซ็ต", "เริ่มใหม่", "ล้างข้อมูล"
    }

    if clean_text in resets:
        reset_session_completely(request)
        msg = "✅ ระบบได้ถูกรีเซ็ตเรียบร้อยแล้วค่ะ เริ่มงานใหม่ได้เลย!" if is_thai else "✅ Session reset. We are ready to start fresh!"
        return Response({"message": msg})

    # ==========================================================
    # 5. NUMBERING SCHEME TOGGLE (ISO19650 4-digit / 5-digit / a49SheetNaming)
    # ==========================================================
    # Quick chat command for opting a new/empty project into a numbering
    # scheme. Auto-detect (resolve_scheme_for_request) takes over once the
    # project has its first sheet of any shape — at that point the override
    # becomes a no-op. Use these to bootstrap empty projects only.
    #
    # Internal scheme keys: iso19650_4digit / iso19650_5digit / a49_dotted.
    # Legacy keys (v1_small / v2_large) from earlier session storage are
    # auto-migrated by resolve_scheme_for_request().

    set_v2 = {
        # ISO19650 phrasings (with and without space, with and without "use"/"switch")
        "use iso19650 5-digit", "use iso 19650 5-digit", "use iso 5-digit",
        "switch to iso19650 5-digit", "switch to iso 19650 5-digit", "switch to iso 5-digit",
        "iso19650 5-digit", "iso 19650 5-digit", "iso 5-digit",
        # Generic digit-only phrasings
        "use 5 digit numbering", "use 5-digit numbering", "use 5 digit", "use 5-digit",
        "switch to 5-digit", "switch to 5 digit",
        "5-digit numbering", "5 digit numbering", "5-digit", "5 digit",
        # Thai
        "ใช้เลขแบบ iso19650 5 หลัก", "ใช้เลขแบบ iso 19650 5 หลัก",
        "ใช้เลข iso19650 5 หลัก", "ใช้เลข iso 5 หลัก",
        "ใช้เลข 5 หลัก", "เลขแบบ 5 หลัก", "เลข 5 หลัก",
    }

    set_v1 = {
        # ISO19650 phrasings
        "use iso19650 4-digit", "use iso 19650 4-digit", "use iso 4-digit",
        "switch to iso19650 4-digit", "switch to iso 19650 4-digit", "switch to iso 4-digit",
        "iso19650 4-digit", "iso 19650 4-digit", "iso 4-digit",
        # Generic digit-only phrasings
        "use 4 digit numbering", "use 4-digit numbering", "use 4 digit", "use 4-digit",
        "switch to 4-digit", "switch to 4 digit",
        "4-digit numbering", "4 digit numbering", "4-digit", "4 digit",
        # Thai
        "ใช้เลขแบบ iso19650 4 หลัก", "ใช้เลขแบบ iso 19650 4 หลัก",
        "ใช้เลข iso19650 4 หลัก", "ใช้เลข iso 4 หลัก",
        "ใช้เลข 4 หลัก", "เลขแบบ 4 หลัก", "เลข 4 หลัก",
    }

    set_a49_dotted = {
        # A49 dotted phrasings (the "a49SheetNaming" user-facing label)
        "use a49 sheet naming", "use a49sheetnaming", "use a49 numbering",
        "use a49 dotted", "use a49 dotted format", "use a49 dot",
        "switch to a49", "switch to a49 sheet naming", "switch to a49 dotted",
        "a49 sheet naming", "a49sheetnaming", "a49 dotted",
        "use dotted", "switch to dotted", "dotted numbering", "dotted",
        # Thai
        "ใช้เลขแบบ a49", "ใช้แบบ a49", "ใช้รูปแบบ a49",
        "เลขแบบ a49", "แบบ a49",
        "ใช้เลขแบบจุด", "เลขแบบจุด",
    }

    inquire_scheme = {
        "what numbering scheme", "what scheme", "which numbering scheme",
        "current scheme", "current numbering scheme",
        "what numbering scheme is active", "show numbering scheme",
        # ISO19650-themed phrasings
        "what iso scheme", "which iso scheme", "show iso scheme",
        "what iso19650 scheme", "current iso scheme",
        # Thai
        "เลขแบบไหน", "ใช้เลขแบบไหน", "ตอนนี้ใช้เลขอะไร",
        "เลข iso แบบไหน", "ใช้เลข iso แบบไหน",
    }

    if clean_text in set_v2 or clean_text in set_v1 or clean_text in set_a49_dotted:
        if clean_text in set_v2:
            target_name = "iso19650_5digit"
        elif clean_text in set_v1:
            target_name = "iso19650_4digit"
        else:
            target_name = "a49_dotted"
        request.session["ai_numbering_scheme"] = target_name
        request.session.modified = True

        # Show the user what will actually be used (auto-detect can override
        # if the project already has sheets of the other shape).
        effective = resolve_scheme_for_request(request)
        effective_name = next(
            (n for n, cfg in SCHEMES.items() if cfg is effective), "unknown")

        target_label = _scheme_label(target_name, thai=False)
        target_label_th = _scheme_label(target_name, thai=True)

        if effective_name == target_name:
            if is_thai:
                msg = f"✅ ตั้งค่าเป็น {target_label_th} เรียบร้อยแล้วค่ะ ใช้กับ Sheet ใหม่ได้เลย!"
            else:
                msg = f"✅ Numbering scheme set to **{target_label}**. New sheets will use this format."
        else:
            # Override was set, but auto-detect won — explain why.
            effective_label = _scheme_label(effective_name, thai=False)
            effective_label_th = _scheme_label(effective_name, thai=True)
            if is_thai:
                msg = (
                    f"⚠️ ตั้งค่าเป็น {target_label_th} แล้ว แต่ระบบตรวจพบว่าโครงการนี้มี Sheet "
                    f"แบบ {effective_label_th} อยู่แล้ว — เพื่อไม่ให้ผสมกัน ระบบจะใช้ {effective_label_th} ต่อค่ะ\n"
                    f"ถ้าต้องการเปลี่ยนจริง ๆ ต้องเปลี่ยนเลข Sheet เก่าให้ตรงกับรูปแบบใหม่ก่อนนะคะ"
                )
            else:
                msg = (
                    f"⚠️ Override saved as **{target_label}**, but this project already has "
                    f"**{effective_label}** sheets — auto-detect wins to prevent mixing.\n"
                    f"Renumber the existing sheets first if you want to switch the whole project."
                )
        return Response({"message": msg, "session_key": request.session.session_key})

    if clean_text in inquire_scheme:
        effective = resolve_scheme_for_request(request)
        effective_name = next(
            (n for n, cfg in SCHEMES.items() if cfg is effective), "unknown")
        override = request.session.get("ai_numbering_scheme")
        cached_sheets = request.session.get("ai_last_known_sheets") or []
        detected = _detect_scheme_from_sheets(cached_sheets)

        # Identify which mechanism actually picked the active scheme.
        if detected and detected in SCHEMES:
            reason = "auto-detected"
            reason_th = "ตรวจพบจาก Sheet ที่มีอยู่"
        elif override and override in SCHEMES:
            reason = "session override"
            reason_th = "ตั้งค่าโดยผู้ใช้"
        else:
            reason = "default"
            reason_th = "ค่าเริ่มต้น"

        scheme_label = _scheme_label(effective_name, thai=False)
        scheme_label_th = _scheme_label(effective_name, thai=True)

        if is_thai:
            msg = (
                f"📐 รูปแบบเลข Sheet ที่ใช้: **{scheme_label_th}**\n"
                f"แหล่งที่มา: {reason_th}"
            )
            if override and detected and override != detected:
                override_label_th = _scheme_label(override, thai=True)
                detected_label_th = _scheme_label(detected, thai=True)
                msg += f"\n(หมายเหตุ: ตั้ง Override เป็น {override_label_th} แต่ตรวจพบ {detected_label_th} ในโครงการ — ระบบใช้ตามที่ตรวจพบ)"
        else:
            msg = (
                f"📐 Active numbering scheme: **{scheme_label}**\n"
                f"Source: {reason}"
            )
            if override and detected and override != detected:
                override_label = _scheme_label(override, thai=False)
                detected_label = _scheme_label(detected, thai=False)
                msg += f"\n(Override is **{override_label}** but project sheets are **{detected_label}** — auto-detect wins.)"

        return Response({"message": msg, "session_key": request.session.session_key})

    # No conversational match found
    return None


def get_fallback_response():
    """Returns a random fallback response when the intent is entirely unknown."""
    fallback_variants = [
        "I’d love to chat, but my brain is 100% wired for Revit right now! Need me to generate some views or sheets?",
        "อยากคุยเล่นด้วยจังค่ะ แต่ตอนนี้สมอง Vella มีแต่เรื่อง Revit เต็มไปหมดเลย! ให้ช่วยสร้าง View หรือ Sheet ดีไหมคะ?",
        "My small-talk capability is currently in development. But my Revit automation is fully charged! What drafting task can we tackle?",
        "ระบบชวนคุยของ Vella ยังไม่เสร็จสมบูรณ์เลยค่ะ แต่ระบบช่วยงาน Revit พร้อมลุย 100%! วันนี้มีงานดราฟต์อะไรให้ช่วยไหมคะ?",
        "Oops! I didn't catch a Revit command in there. My conversational skills are still under construction, but I can definitely organize those sheets for you!",
        "อุ๊ย! Vella หาคำสั่ง Revit ไม่เจอเลยค่ะ ทักษะการคุยเล่นยังอยู่ในช่วงก่อสร้าง แต่ถ้าให้ช่วยจัดการ Sheet นี่ยินดีเลยนะคะ!"
    ]
    return random.choice(fallback_variants)