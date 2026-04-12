from django.contrib import admin
from django.urls import path, include 

urlpatterns = [
    path('admin/', admin.site.urls),

    # AI Router endpoint
    path('api/ai/', include('ai_router.urls')),  
]

