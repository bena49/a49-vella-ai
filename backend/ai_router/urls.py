from django.urls import path
from .views import ai_router

urlpatterns = [
    path('', ai_router, name='ai_router'),
]
