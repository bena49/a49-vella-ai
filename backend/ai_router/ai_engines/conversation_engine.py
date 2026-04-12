import random
from rest_framework.response import Response
from ..ai_core.session_manager import reset_session_completely, reset_pending

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